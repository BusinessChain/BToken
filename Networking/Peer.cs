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

      bool IsSessionExecuting = false;
      BufferBlock<NetworkMessage> SessionMessageBuffer = new BufferBlock<NetworkMessage>();

      TcpClient TcpClient;
      MessageStreamer NetworkMessageStreamer;

      public uint PenaltyScore { get; private set; }



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

        Task peerStartTask = ProcessNetworkMessageAsync();
        Task sessionListenerTask = StartSessionListenerAsync();
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
            Debug.WriteLine("Network::ConnectAsync: " + ex.Message
              + "\nConnection tries: '{0}'", ++connectionTries);
          }
        }
      }
      async Task StartSessionListenerAsync()
      {
        while (true)
        {
          INetworkSession session = await Network.NetworkSessionQueue.ReceiveAsync().ConfigureAwait(false);
          await ExecuteSessionAsync(session);
        }
      }

      public async Task ConnectTCPAsync()
      {
        TcpClient = new TcpClient();
        await TcpClient.ConnectAsync(IPEndPoint.Address, IPEndPoint.Port).ConfigureAwait(false);
        NetworkMessageStreamer = new MessageStreamer(TcpClient.GetStream());
      }
      public async Task HandshakeAsync()
      {
        await NetworkMessageStreamer.WriteAsync(new VersionMessage()).ConfigureAwait(false);
        
        CancellationToken cancellationToken = new CancellationTokenSource(TimeSpan.FromSeconds(2)).Token;

        var handshakeManager = new PeerHandshakeManager(this);
        while (!handshakeManager.isHandshakeCompleted())
        {
          NetworkMessage messageRemote = await NetworkMessageStreamer.ReadAsync(cancellationToken).ConfigureAwait(false);
          await handshakeManager.ProcessResponseToVersionMessageAsync(messageRemote).ConfigureAwait(false);
        }
      }
      public async Task ProcessNetworkMessageAsync(CancellationToken cancellationToken = default(CancellationToken))
      {
        try
        {
          while (true)
          {
            NetworkMessage networkMessage = await NetworkMessageStreamer.ReadAsync(cancellationToken).ConfigureAwait(false);

            switch (networkMessage.Command)
            {
              case "ping":
                await ProcessPingMessageAsync(networkMessage).ConfigureAwait(false);
                break;
              case "addr":
                ProcessAddressMessage(networkMessage);
                break;
              case "sendheaders":
                await ProcessSendHeadersMessageAsync(networkMessage).ConfigureAwait(false);
                break;
              case "inv":
                await ProcessInventoryMessageAsync(networkMessage).ConfigureAwait(false);
                break;
              case "headers":
                await ProcessHeadersMessageAsync(networkMessage).ConfigureAwait(false);
                break;
              case "getheaders":
                await ProcessGetHeadersMessageAsync(networkMessage).ConfigureAwait(false);
                break;
              case "block":
                await ProcessBlockMessageAsync(networkMessage).ConfigureAwait(false);
                break;
              default:
                break;
            }
          }
        }
        catch (Exception ex)
        {
          Debug.WriteLine("Peer::ProcessMessagesAsync: " + ex.Message);
          Dispose();
        }
      }
      void ProcessAddressMessage(NetworkMessage networkMessage)
      {
        AddressMessage addressMessage = new AddressMessage(networkMessage);
      }
      async Task ProcessPingMessageAsync(NetworkMessage networkMessage)
      {
        PingMessage pingMessage = new PingMessage(networkMessage);
        await NetworkMessageStreamer.WriteAsync(new PongMessage(pingMessage.Nonce)).ConfigureAwait(false);
      }
      async Task ProcessSendHeadersMessageAsync(NetworkMessage networkMessage) => await NetworkMessageStreamer.WriteAsync(new SendHeadersMessage()).ConfigureAwait(false);
      async Task ProcessInventoryMessageAsync(NetworkMessage networkMessage)
      {
        InvMessage invMessage = new InvMessage(networkMessage);

        if (invMessage.GetBlockInventories().Any()) // direkt als property zu kreationszeit anlegen.
        {
          await Network.NetworkMessageBufferBlockchain.SendAsync(invMessage).ConfigureAwait(false);
        }
        if (invMessage.GetTXInventories().Any())
        {
          await Network.NetworkMessageBufferUTXO.SendAsync(invMessage).ConfigureAwait(false);
        };
      }
      async Task ProcessHeadersMessageAsync(NetworkMessage networkMessage)
      {
        var headersMessage = new HeadersMessage(networkMessage);
        await BufferMessageAsync(headersMessage);
      }

      async Task ProcessBlockMessageAsync(NetworkMessage networkMessage)
      {
        var blockMessage = new BlockMessage(networkMessage);
        await BufferMessageAsync(blockMessage);
      } 

      async Task BufferMessageAsync(NetworkMessage networkMessage)
      {
        if (IsSessionExecuting)
        {
          await SessionMessageBuffer.SendAsync(networkMessage).ConfigureAwait(false);
        }
        else
        {
          await Network.NetworkMessageBufferBlockchain.SendAsync(networkMessage).ConfigureAwait(false);
        }
      }

      async Task ProcessGetHeadersMessageAsync(NetworkMessage networkMessage)
      {
        GetHeadersMessage getHeadersMessage = new GetHeadersMessage(networkMessage);
      }

      public async Task SendMessageAsync(NetworkMessage networkMessage) => await NetworkMessageStreamer.WriteAsync(networkMessage).ConfigureAwait(false);
           
      public void Blame(uint penaltyScore)
      {
        PenaltyScore += penaltyScore;

        if (PenaltyScore >= 100)
        {
          Network.AddressPool.Blame(IPEndPoint.Address);
          Dispose();
        }
      }
          
      public void Dispose()
      {
        TcpClient.Close();
      }

      public async Task ExecuteSessionAsync(INetworkSession session)
      {
        IsSessionExecuting = true;
        int sessionExcecutionTries = 0;

        while (true)
        {
          try
          {
            await session.StartAsync(this);
          }
          catch (Exception ex)
          {
            IsSessionExecuting = false;

            Debug.WriteLine("Peer::ExecuteSessionAsync:" + ex.Message +
            ", Session excecution tries: '{0}'", ++sessionExcecutionTries);

            Dispose();

            await ConnectAsync().ConfigureAwait(false);
          }

          IsSessionExecuting = false;
          return;
        }
      }

      public async Task PingAsync() => await NetworkMessageStreamer.WriteAsync(new PingMessage(Nonce));

      public async Task<NetworkBlock> GetBlockAsync(UInt256 hash, CancellationToken cancellationToken)
      {
        var inventory = new Inventory(InventoryType.MSG_BLOCK, hash);
        await NetworkMessageStreamer.WriteAsync(new GetDataMessage(new List<Inventory>() { inventory })).ConfigureAwait(false);

        while (true)
        {
          NetworkMessage networkMessage = await SessionMessageBuffer.ReceiveAsync(cancellationToken).ConfigureAwait(false);
          var blockMessage = networkMessage as BlockMessage;

          if (blockMessage != null)
          {
            return blockMessage.NetworkBlock;
          }
        }
      }
      public async Task<List<NetworkHeader>> GetHeadersAsync(List<UInt256> headerLocator)
      {
        await NetworkMessageStreamer.WriteAsync(new GetHeadersMessage(headerLocator)).ConfigureAwait(false);

        CancellationToken cancellationToken = new CancellationTokenSource(TimeSpan.FromSeconds(2)).Token;
        HeadersMessage headersMessage = await ReceiveHeadersMessageAsync(cancellationToken).ConfigureAwait(false);
        return headersMessage.Headers;
      }
      async Task<HeadersMessage> ReceiveHeadersMessageAsync(CancellationToken cancellationToken)
      {
        while (true)
        {
          NetworkMessage networkMessage = await SessionMessageBuffer.ReceiveAsync(cancellationToken).ConfigureAwait(false);
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
