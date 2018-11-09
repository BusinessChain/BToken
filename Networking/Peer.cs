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
      PeerHandshakeManager HandshakeManager;

      TcpClient TcpClient;
      MessageStreamer NetworkMessageStreamer;
            
      public uint PenaltyScore { get; private set; }



      public Peer(Network network)
      {
        Network = network;

        HandshakeManager = new PeerHandshakeManager(this);
      }
      public Peer(TcpClient tcpClient, Network network)
      {
        Network = network;

        TcpClient = tcpClient;
        NetworkMessageStreamer = new MessageStreamer(TcpClient.GetStream());

        HandshakeManager = new PeerHandshakeManager(this);
        IPEndPoint = (IPEndPoint)tcpClient.Client.RemoteEndPoint;
      }


      public async Task ConnectAsync()
      {
        IPAddress iPAddress = Network.AddressPool.GetRandomNodeAddress();
        IPEndPoint = new IPEndPoint(iPAddress, Port);
        await ConnectTCPAsync().ConfigureAwait(false);
        await HandshakeAsync().ConfigureAwait(false);
        Task peerStartTask = ProcessNetworkMessageAsync();
        Task sessionListenerTask = StartSessionListenerAsync();
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

        while (!HandshakeManager.isHandshakeCompleted())
        {
          NetworkMessage messageRemote = await NetworkMessageStreamer.ReadAsync(cancellationToken).ConfigureAwait(false);
          await HandshakeManager.ProcessResponseToVersionMessageAsync(messageRemote).ConfigureAwait(false);
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
      async Task ProcessHeadersMessageAsync(NetworkMessage networkMessage) => await Network.NetworkMessageBufferBlockchain.SendAsync(new HeadersMessage(networkMessage)).ConfigureAwait(false);
      async Task ProcessBlockMessageAsync(NetworkMessage networkMessage) => await Network.NetworkMessageBufferBlockchain.SendAsync(new BlockMessage(networkMessage)).ConfigureAwait(false);
      public bool IsOwnerOfBuffer(BufferBlock<NetworkMessage> buffer) => buffer == Network.NetworkMessageBufferBlockchain || buffer == Network.NetworkMessageBufferUTXO;

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
        int sessionExcecutionTries = 0;

        while (true)
        {
          try
          {
            await session.StartAsync(this);
            return;
          }
          catch (Exception ex)
          {
            Debug.WriteLine("Peer::ExecuteSessionAsync:" + ex.Message +
            ", Session excecution tries: '{0}'", ++sessionExcecutionTries);

            Dispose();

            await ConnectAsync().ConfigureAwait(false);
          }
        }
      }

      public async Task PingAsync() => await NetworkMessageStreamer.WriteAsync(new PingMessage(Nonce));

      public async Task GetBlocksAsync(List<UInt256> hashes)
      {
        List<Inventory> inventories = hashes.Select(h => new Inventory(InventoryType.MSG_BLOCK, h)).ToList();
        await NetworkMessageStreamer.WriteAsync(new GetDataMessage(inventories)).ConfigureAwait(false);
      }
      public async Task<List<NetworkHeader>> GetHeadersAsync(List<UInt256> headerLocator)
      {
        await NetworkMessageStreamer.WriteAsync(new GetHeadersMessage(headerLocator)).ConfigureAwait(false);

        CancellationToken cancellationToken = new CancellationTokenSource(TimeSpan.FromSeconds(2)).Token;
        Network.HeadersMessage headersMessage = await GetHeadersMessageAsync(cancellationToken).ConfigureAwait(false);
        return headersMessage.Headers;
      }
      async Task<HeadersMessage> GetHeadersMessageAsync(CancellationToken cancellationToken)
      {
        while (true)
        {
          NetworkMessage networkMessage = await NetworkMessageStreamer.ReadAsync(cancellationToken).ConfigureAwait(false);
          Network.HeadersMessage headersMessage = networkMessage as Network.HeadersMessage;

          if (headersMessage != null)
          {
            return headersMessage;
          }
        }
      }


      public async Task RequestBlocksAsync(List<UInt256> headerHashes)
      {
        throw new NotImplementedException();
      }
      public async Task<NetworkMessage> GetNetworkMessageAsync(CancellationToken token)
      {
        throw new NotImplementedException();
      }

    }
  }
}
