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

    public const int PEERS_COUNT_OUTBOUND = 1;

    List<Peer> Peers = new List<Peer>();
    
    BufferBlock<NetworkMessage> NetworkMessageBufferUTXO = new BufferBlock<NetworkMessage>();
    BufferBlock<NetworkMessage> NetworkMessageBufferBlockchain = new BufferBlock<NetworkMessage>();


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
        var peer = new Peer(this);
        Peers.Add(peer);
      }
    }

    public void Start()
    {
      StartPeers();

      //Task peerInboundListenerTask = StartPeerInboundListenerAsync();
    }
    void StartPeers()
    {
      Peers.Select(async peer =>
      {
        Task startPeerTask = peer.StartAsync();
      }).ToArray();
    }

    public async Task SendSessionAsync(INetworkSession session)
    {
      var peer = GetPeerSmallestSessionQueue();

      while(peer == null)
      {
        await Task.Delay(1000).ConfigureAwait(false);
        peer = GetPeerSmallestSessionQueue();
      }

      peer.PostSession(session);
    }
    Peer GetPeerSmallestSessionQueue()
    {
      Peer peerSmallestSessionQueue = null;

      foreach(Peer peer in Peers)
      {
        if(!peer.IsSessionRunning && peer.PeerSessionQueue.Count == 0)
        {
          peerSmallestSessionQueue = peer;
          break;
        }
        else 
        {
          if(peerSmallestSessionQueue == null || peer.PeerSessionQueue.Count < peerSmallestSessionQueue.PeerSessionQueue.Count)
          {
            peerSmallestSessionQueue = peer;
          }
        }
      }

      return peerSmallestSessionQueue;
    }
    
    public async Task StartPeerInboundListenerAsync()
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

          await peer.StartAsync().ConfigureAwait(false);
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
    
    static long GetUnixTimeSeconds() => DateTimeOffset.UtcNow.ToUnixTimeSeconds();

    public async Task PingAsync()
    {
      foreach (Peer peer in Peers)
      {
        await peer.PingAsync().ConfigureAwait(false);
      }
    }

    public async Task<NetworkBlock> GetBlockAsync(UInt256 blockHash)
    {
      CancellationToken cancellationToken = new CancellationTokenSource(TimeSpan.FromSeconds(10)).Token;
      return await Peers.First().GetBlockAsync(blockHash, cancellationToken).ConfigureAwait(false);
    }

  }
}
