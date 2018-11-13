using System.Diagnostics;

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using System.Threading.Tasks.Dataflow;

using BToken.Chaining;
using BToken.Accounting;

namespace BToken.Networking
{
  public partial class Network : Chaining.INetwork, Accounting.INetwork
  {
    const UInt16 Port = 8333;
    const UInt32 ProtocolVersion = 70013;
    const ServiceFlags NetworkServicesRemoteRequired = ServiceFlags.NODE_NETWORK;
    const ServiceFlags NetworkServicesLocalProvided = ServiceFlags.NODE_NETWORK;
    const string UserAgent = "/BToken:0.0.0/";
    const Byte RelayOption = 0x00;
    
    static UInt64 Nonce;
    NetworkAddressPool AddressPool;

    TcpListener TcpListener;
    public const int PEERS_COUNT_INBOUND = 8;
    List<Peer> PeersInbound = new List<Peer>();

    public const int PEERS_COUNT_OUTBOUND = 8;
    List<Peer> PeersOutbound = new List<Peer>();

    BufferBlock<INetworkSession> NetworkSessionQueue = new BufferBlock<INetworkSession>();
    
    public BufferBlock<NetworkMessage> NetworkMessageBufferUTXO = new BufferBlock<NetworkMessage>();
    public BufferBlock<NetworkMessage> NetworkMessageBufferBlockchain = new BufferBlock<NetworkMessage>();

    public Network()
    {
      Nonce = createNonce();
      AddressPool = new NetworkAddressPool();
      
      TcpListener = new TcpListener(IPAddress.Any, Port);

      CreatePeersOutbound();
    }
    static ulong createNonce()
    {
      Random rnd = new Random();

      ulong number = (ulong)rnd.Next();
      number = number << 32;
      return number |= (uint)rnd.Next();
    }
    void CreatePeersOutbound()
    {
      for (int i = 0; i < PEERS_COUNT_OUTBOUND; i++)
      {
        PeersOutbound.Add(new Peer(this));
      }
    }

    public void Start()
    {
      StartPeers();
      Task inboundPeerListenerTask = StartInboundPeerListenerAsync();
    }
    void StartPeers()
    {
      PeersOutbound.Select(async peer =>
      {
        await peer.ConnectAsync();
      }).ToArray();
    }

    public async Task StartInboundPeerListenerAsync()
    {
      TcpListener.Start(PEERS_COUNT_INBOUND);

      while (true)
      {
        try
        {
          TcpClient client = await TcpListener.AcceptTcpClientAsync();
          Debug.WriteLine("received inbound request from " + client.Client.RemoteEndPoint.ToString());
          Peer peer = new Peer(client, this);
          PeersInbound.Add(peer);

          await peer.ConnectAsync().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
          Debug.WriteLine(ex.Message);
        }
      }
    }

    public async Task<NetworkMessage> GetMessageBlockchainAsync()
    {
      return await NetworkMessageBufferBlockchain.ReceiveAsync();
    }
    public async Task<NetworkMessage> GetMessageBitcoinAsync()
    {
      return await NetworkMessageBufferUTXO.ReceiveAsync();
    }

    public void PostSession(INetworkSession session)
    {
      // allenfalls könnte jeder Peer einen eigenen Queue haben, damit könnten 
      // Broadcast versendet werden und einzelne Peers angesprochen werden.
      NetworkSessionQueue.Post(session);
    }
    public async Task PostSessionAsync(INetworkSession session)
    {

    }

    static long getUnixTimeSeconds() => DateTimeOffset.UtcNow.ToUnixTimeSeconds();

    public async Task PingAsync()
    {
      foreach (Peer peer in PeersOutbound)
      {
        await peer.PingAsync().ConfigureAwait(false);
      }
    }

    public async Task GetBlocksAsync(List<UInt256> blockHashes)
    {
      await PeersOutbound.First().GetBlocksAsync(blockHashes).ConfigureAwait(false);
    }

  }
}
