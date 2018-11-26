using System.Diagnostics;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using System.Threading.Tasks.Dataflow;

using BToken.Chaining;

namespace BToken.Networking
{
  partial class Network
  {
    partial class Peer : IDisposable, INetworkChannel
    {
      Network Network;

      IPEndPoint IPEndPoint;
      TcpClient TcpClient;
      MessageStreamer NetworkMessageStreamer;
      BufferBlock<NetworkMessage> NetworkMessageBuffer = new BufferBlock<NetworkMessage>();
      BufferBlock<NetworkMessage> SessionMessageBuffer = new BufferBlock<NetworkMessage>();
      
      public BufferBlock<NetworkMessage> InboundSessionRequestBuffer { get; private set; } = new BufferBlock<NetworkMessage>();
      public BufferBlock<INetworkSession> PeerSessionQueue { get; private set; } = new BufferBlock<INetworkSession>();
      public bool IsSessionRunning { get; private set; }


      public Peer(Network network)
      {
        Network = network;
      }
      public Peer(TcpClient tcpClient, Network network)
      {
        Network = network;

        TcpClient = tcpClient;
        NetworkMessageStreamer = new MessageStreamer(TcpClient.GetStream());

        IPEndPoint = (IPEndPoint)tcpClient.Client.RemoteEndPoint;
      }
      
      public async Task StartAsync()
      {
        await ConnectAsync();

        Task processPeerMessagesTask = ProcessNetworkMessagesAsync();
        Task startApplicationSessionListenerTask = StartApplicationSessionListenerAsync();

      }

      public void PostSession(INetworkSession session)
      {
        PeerSessionQueue.Post(session);
      }
      async Task StartApplicationSessionListenerAsync()
      {
        while (true)
        {
          INetworkSession session = await PeerSessionQueue.ReceiveAsync(); // wie sehen wir, ob session wiederholt wird?
          while (!await RunSessionAsync(session));
        }
      }

      async Task<bool> RunSessionAsync(INetworkSession session)
      {
        bool isSessionRunSuccess;

        try
        {
          IsSessionRunning = true;

          await session.RunAsync(this);

          IsSessionRunning = false;
          isSessionRunSuccess = true;
        }
        catch (Exception ex)
        {
          IsSessionRunning = false;
          isSessionRunSuccess = false;

          Console.WriteLine("Session '{0}' with peer '{1}' ended with exception: \n'{2}'",
            session.GetType().ToString(),
            IPEndPoint.Address.ToString(),
            ex.Message);

          Dispose();

          await ConnectAsync();
        }

        return isSessionRunSuccess;
      }
      async Task ConnectAsync()
      {
        int connectionTries = 0;

        while(true)
        {
          try
          {
            IPAddress iPAddress = Network.AddressPool.GetRandomNodeAddress();
            IPEndPoint = new IPEndPoint(iPAddress, Port);
            await ConnectTCPAsync().ConfigureAwait(false);
            await HandshakeAsync().ConfigureAwait(false);

            return;
          }
          catch (Exception ex)
          {
            Debug.WriteLine("Peer::ConnectAsync: " + ex.Message
              + "\nConnection tries: '{0}'", ++connectionTries);
          }
        }
      }



      async Task ProcessNetworkMessagesAsync()
      {
        while (true)
        {
          try
          {
            NetworkMessage networkMessage = await NetworkMessageStreamer.ReadAsync(default(CancellationToken));

            switch (networkMessage.Command)
            {
              case "version":
                await ProcessVersionMessageAsync(networkMessage);
                break;
              case "ping":
                await ProcessPingMessageAsync(networkMessage);
                break;
              case "addr":
                ProcessAddressMessage(networkMessage);
                break;
              case "sendheaders":
                await ProcessSendHeadersMessageAsync(networkMessage);
                break;
              default:
                await ProcessApplicationMessageAsync(networkMessage);
                break;
            }
          }
          catch (Exception ex)
          {
            Console.WriteLine("Processing of peer messages aborted with peer '{0}' due to exception: \n'{1}'",
              IPEndPoint.Address.ToString(),
              ex.Message);

            Dispose();

            await ConnectAsync();
          }
        }
      }
      async Task ProcessApplicationMessageAsync(NetworkMessage networkMessage)
      {
        if (IsSessionRunning)
        {
          await SessionMessageBuffer.SendAsync(networkMessage);
        }
        else
        {
          await InboundSessionRequestBuffer.SendAsync(networkMessage);          
        }
      }


      async Task ConnectTCPAsync()
      {
        TcpClient = new TcpClient();
        await TcpClient.ConnectAsync(IPEndPoint.Address, IPEndPoint.Port);
        NetworkMessageStreamer = new MessageStreamer(TcpClient.GetStream());
      }
      async Task HandshakeAsync()
      {
        await NetworkMessageStreamer.WriteAsync(new VersionMessage());
        
        CancellationToken cancellationToken = new CancellationTokenSource(TimeSpan.FromSeconds(2)).Token;

        var handshakeManager = new PeerHandshakeManager(this);
        while (!handshakeManager.isHandshakeCompleted())
        {
          NetworkMessage messageRemote = await NetworkMessageStreamer.ReadAsync(cancellationToken);
          await handshakeManager.ProcessResponseToVersionMessageAsync(messageRemote);
        }
      }
      async Task ProcessVersionMessageAsync(NetworkMessage networkMessage)
      {
      }
      async Task ProcessPingMessageAsync(NetworkMessage networkMessage)
      {
        PingMessage pingMessage = new PingMessage(networkMessage);
        await NetworkMessageStreamer.WriteAsync(new PongMessage(pingMessage.Nonce)).ConfigureAwait(false);
      }
      void ProcessAddressMessage(NetworkMessage networkMessage)
      {
        AddressMessage addressMessage = new AddressMessage(networkMessage);
      }
      async Task ProcessSendHeadersMessageAsync(NetworkMessage networkMessage) => await NetworkMessageStreamer.WriteAsync(new SendHeadersMessage()).ConfigureAwait(false);
      //async Task ProcessInventoryMessageAsync(NetworkMessage networkMessage)
      //{
      //  InvMessage invMessage = new InvMessage(networkMessage);

      //  if (invMessage.GetBlockInventories().Any()) // direkt als property zu kreationszeit anlegen.
      //  {
      //    await Network.NetworkMessageBufferBlockchain.SendAsync(invMessage).ConfigureAwait(false);
      //  }
      //  if (invMessage.GetTXInventories().Any())
      //  {
      //    await Network.NetworkMessageBufferUTXO.SendAsync(invMessage).ConfigureAwait(false);
      //  };
      //}
      
      public async Task SendMessageAsync(NetworkMessage networkMessage) => await NetworkMessageStreamer.WriteAsync(networkMessage).ConfigureAwait(false);

      public void Dispose()
      {
        TcpClient.Close();
      }
      
      public async Task PingAsync() => await NetworkMessageStreamer.WriteAsync(new PingMessage(Nonce));

      public async Task<NetworkBlock> GetBlockAsync(UInt256 hash, CancellationToken cancellationToken)
      {
        var inventory = new Inventory(InventoryType.MSG_BLOCK, hash);
        await NetworkMessageStreamer.WriteAsync(new GetDataMessage(new List<Inventory>() { inventory }));

        while (true)
        {
          NetworkMessage networkMessage = await SessionMessageBuffer.ReceiveAsync(cancellationToken);
          var blockMessage = networkMessage as BlockMessage;

          if (blockMessage != null)
          {
            return blockMessage.NetworkBlock;
          }
        }
      }
      public async Task<List<NetworkHeader>> GetHeadersAsync(List<UInt256> headerLocator)
      {
        await NetworkMessageStreamer.WriteAsync(new GetHeadersMessage(headerLocator));

        CancellationToken cancellationToken = new CancellationTokenSource(TimeSpan.FromSeconds(2)).Token;
        HeadersMessage headersMessage = await ReceiveHeadersMessageAsync(cancellationToken);
        return headersMessage.Headers;
      }
      async Task<HeadersMessage> ReceiveHeadersMessageAsync(CancellationToken cancellationToken)
      {
        while (true)
        {
          NetworkMessage networkMessage = await SessionMessageBuffer.ReceiveAsync(cancellationToken);
          HeadersMessage headersMessage = networkMessage as HeadersMessage;

          if (headersMessage != null)
          {
            return headersMessage;
          }
        }
      }

    }
  }
}
