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

      CancellationTokenSource CancellationProcessNetworkMessages;
      public bool IsSessionRunning = false;
      public BufferBlock<INetworkSession> PeerSessionQueue { get; private set; } = new BufferBlock<INetworkSession>();



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

        Task processPeerMessageTask = ProcessPeerMessagesAsync();

        StartProcessNetworkMessages();
      }
      void StartProcessNetworkMessages()
      {
        IsSessionRunning = false;
        Task processNetworkMessageTask = ProcessNetworkMessagesAsync();
      }
      void StopProcessNetworkMessages()
      {
        IsSessionRunning = true;
        CancellationProcessNetworkMessages.Cancel();
      }

      public  async Task ConnectAsync()
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
            Debug.WriteLine("Network::ConnectAsync: " + ex.Message
              + "\nConnection tries: '{0}'", ++connectionTries);
          }
        }
      }

      public void PostSession(INetworkSession session)
      {
        PeerSessionQueue.Post(session);
      }
      public async Task ExecuteSessionAsync(INetworkSession session)
      {
        StopProcessNetworkMessages();

        while (true)
        {
          try
          {
            await session.RunAsync(this);
          }
          catch (Exception ex)
          {
            Debug.WriteLine("Peer::ExecuteSessionAsync:" + ex.Message);

            Dispose();

            await ConnectAsync();

            continue;
          }
          break;
        }

        StartProcessNetworkMessages();
      }


      async Task ProcessPeerMessagesAsync()
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
                await NetworkMessageBuffer.SendAsync(networkMessage);
                break;
            }
          }
          catch (Exception ex)
          {
            Debug.WriteLine("Peer::ProcessNetworkMessageAsync: " + ex.Message);

            Dispose();

            await ConnectAsync();
          }
        }
      }

      async Task ProcessNetworkMessagesAsync()
      {
        try
        {
          CancellationProcessNetworkMessages = new CancellationTokenSource();

          while (true)
          {
            NetworkMessage networkMessage = await NetworkMessageBuffer.ReceiveAsync(CancellationProcessNetworkMessages.Token);

            Network.SignalPeerIdle.Receive();
            IsSessionRunning = true;

            INetworkSession session = await Network.Blockchain.RequestSessionAsync(networkMessage, default(CancellationToken));
            await StartSessionAsync(session);

            IsSessionRunning = false;
            Network.SignalPeerIdle.Post(true);
          }
        }
        catch(Exception ex)
        {
          Console.WriteLine("Process NetworkMessages was '{0}' with peer '{1}' ended with exception: \n'{2}'",
            session.GetType().ToString(),
            IPEndPoint.Address.ToString(),
            ex.Message);
        }
      }
      async Task StartSessionAsync(INetworkSession session)
      {
        try
        {
          await session.RunAsync(this);
        }
        catch (Exception ex)
        {
          Console.WriteLine("Session '{0}' with peer '{1}' ended with exception: \n'{2}'", 
            session.GetType().ToString(),
            IPEndPoint.Address.ToString(),
            ex.Message);
        }
      }
      

      public async Task ConnectTCPAsync()
      {
        TcpClient = new TcpClient();
        await TcpClient.ConnectAsync(IPEndPoint.Address, IPEndPoint.Port);
        NetworkMessageStreamer = new MessageStreamer(TcpClient.GetStream());
      }
      public async Task HandshakeAsync()
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
          NetworkMessage networkMessage = await NetworkMessageBuffer.ReceiveAsync(cancellationToken);
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
          NetworkMessage networkMessage = await NetworkMessageBuffer.ReceiveAsync(cancellationToken);
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
