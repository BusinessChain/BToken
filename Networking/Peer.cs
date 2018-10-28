using System.Diagnostics;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using System.Threading.Tasks.Dataflow;

namespace BToken.Networking
{
  partial class Network
  {
    partial class Peer : IDisposable
    {
      Network Network;
      IPEndPoint IPEndPoint;
      PeerHandshakeManager ConnectionManager;

      TcpClient TcpClient;
      MessageStreamer NetworkMessageStreamer;

      public BufferBlock<NetworkMessage> NetworkMessageBufferUTXO = new BufferBlock<NetworkMessage>();
      public BufferBlock<NetworkMessage> NetworkMessageBufferBlockchain = new BufferBlock<NetworkMessage>();
      
      public uint PenaltyScore { get; private set; }



      public Peer(IPEndPoint ipEndPoint, Network network)
      {
        Network = network;

        ConnectionManager = new PeerHandshakeManager(this);
        IPEndPoint = ipEndPoint;
      }
      public Peer(TcpClient tcpClient, Network network)
      {
        Network = network;

        TcpClient = tcpClient;
        NetworkMessageStreamer = new MessageStreamer(TcpClient.GetStream());

        ConnectionManager = new PeerHandshakeManager(this);
        IPEndPoint = (IPEndPoint)tcpClient.Client.RemoteEndPoint;
      }


      public async Task StartAsync()
      {
        try
        {
          while (true)
          {
            await ProcessNetworkMessageAsync().ConfigureAwait(false);
          }
        }
        catch (Exception ex)
        {
          Debug.WriteLine("Peer::ProcessMessagesAsync: " + ex.Message);
          Dispose();
        }
      }

      public async Task ConnectTCPAsync()
      {
        TcpClient = new TcpClient();
        await TcpClient.ConnectAsync(IPEndPoint.Address, IPEndPoint.Port).ConfigureAwait(false);
        NetworkMessageStreamer = new MessageStreamer(TcpClient.GetStream());
      }
      public async Task HandshakeAsync(uint blockchainHeightLocal)
      {
        await NetworkMessageStreamer.WriteAsync(new VersionMessage(blockchainHeightLocal)).ConfigureAwait(false);
        
        CancellationToken cancellationToken = new CancellationTokenSource(TimeSpan.FromSeconds(2)).Token;

        while (!ConnectionManager.isHandshakeCompleted())
        {
          NetworkMessage messageRemote = await NetworkMessageStreamer.ReadAsync(cancellationToken).ConfigureAwait(false);
          await ConnectionManager.ProcessResponseToVersionMessageAsync(messageRemote).ConfigureAwait(false);
        }
      }
      async Task ProcessNetworkMessageAsync(CancellationToken cancellationToken = default(CancellationToken))
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
          await NetworkMessageBufferBlockchain.SendAsync(invMessage).ConfigureAwait(false);
        }
        if (invMessage.GetTXInventories().Any())
        {
          await NetworkMessageBufferUTXO.SendAsync(invMessage).ConfigureAwait(false);
        };
      }
      async Task ProcessHeadersMessageAsync(NetworkMessage networkMessage) => await NetworkMessageBufferBlockchain.SendAsync(new HeadersMessage(networkMessage)).ConfigureAwait(false);
      async Task ProcessBlockMessageAsync(NetworkMessage networkMessage) => await NetworkMessageBufferBlockchain.SendAsync(new BlockMessage(networkMessage)).ConfigureAwait(false);
      public bool IsOwnerOfBuffer(BufferBlock<NetworkMessage> buffer) => buffer == NetworkMessageBufferBlockchain || buffer == NetworkMessageBufferUTXO;

      public async Task SendMessageAsync(NetworkMessage networkMessage) => await NetworkMessageStreamer.WriteAsync(networkMessage).ConfigureAwait(false);

      public async Task GetHeadersAsync(List<UInt256> headerLocator) => await NetworkMessageStreamer.WriteAsync(new GetHeadersMessage(headerLocator)).ConfigureAwait(false);

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

        NetworkMessageBufferBlockchain.Post(null);
        NetworkMessageBufferUTXO.Post(null);

        Network.PeersOutbound.Remove(this);
      }

      public async Task PingAsync() => await NetworkMessageStreamer.WriteAsync(new PingMessage(Nonce));

      public async Task GetBlocksAsync(List<UInt256> hashes)
      {
        List<Inventory> inventories = hashes.Select(h => new Inventory(InventoryType.MSG_BLOCK, h)).ToList();
        await NetworkMessageStreamer.WriteAsync(new GetDataMessage(inventories)).ConfigureAwait(false);
      }
    }
  }
}
