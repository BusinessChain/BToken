using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using System.Threading.Tasks.Dataflow;


namespace BToken.Networking
{
  public partial class Network
  {
    const UInt16 Port = 8333;
    const UInt32 ProtocolVersion = 70013;
    const ServiceFlags NetworkServicesRemoteRequired = ServiceFlags.NODE_NETWORK;
    const ServiceFlags NetworkServicesLocalProvided = ServiceFlags.NODE_NETWORK;
    const string UserAgent = "/BToken:0.0.0/";
    const Byte RelayOption = 0x00;
    const int PEERS_COUNT_OUTBOUND = 8;
    const int PEERS_COUNT_INBOUND = 8;

    static UInt64 Nonce;

    NetworkAddressPool AddressPool;
    TcpListener TcpListener;


    readonly object ListPeersOutboundLOCK = new object();
    BufferBlock<Peer> PeersOutboundAvailable = new BufferBlock<Peer>();
    List<Peer> PeersInbound = new List<Peer>();
    
    BufferBlock<Peer> PeersRequestInbound = new BufferBlock<Peer>();


    public Network()
    {
      Nonce = createNonce();
      AddressPool = new NetworkAddressPool();
      
      TcpListener = new TcpListener(IPAddress.Any, Port);

      CreatePeers();
    }
    static ulong createNonce()
    {
      Random rnd = new Random();

      ulong number = (ulong)rnd.Next();
      number = number << 32;
      return number |= (uint)rnd.Next();
    }
    void CreatePeers()
    {
      for (int i = 0; i < PEERS_COUNT_OUTBOUND; i++)
      {
        Task createPeerTask = CreatePeerAsync();
      }
    }
    async Task CreatePeerAsync()
    {
      Peer peer;

      do
      {
        peer = new Peer(this);
      } while (!await peer.TryConnectAsync());

      PeersOutboundAvailable.Post(peer);
    }

    public void Start()
    {
      //Task peerInboundListenerTask = StartPeerInboundListenerAsync();
    }

    public async Task RunSessionAsync(INetworkSession session)
    {
      Peer peer = PeersOutboundAvailable.Receive();

      while (true)
      {
        if (await peer.TryExecuteSessionAsync(session).ConfigureAwait(false))
        {
          await PeersOutboundAvailable.SendAsync(peer);
          return;
        }

        do
        {
          peer = new Peer(this);
        } while (!await peer.TryConnectAsync());

      }
    }

    //public void RemoveChannel(INetworkChannel channel)
    //{
    //  Peer peer = (Peer)channel;

    //  if (IsPeerInbound(peer))
    //  {
    //    PeersInbound.Remove(peer);
    //  }
    //  else
    //  {
    //    ReplacePeerOutbound(peer);
    //  }
    //}
    //Peer ReplacePeerOutbound(Peer peer)
    //{
    //  var peerNew = new Peer(this);

    //  lock(ListPeersOutboundLOCK)
    //  {
    //    int indexPeer = PeersOutbound.FindIndex(p => p == peer);
    //    if(indexPeer < 0)
    //    {
    //      PeersOutbound.Add(peerNew);
    //    }
    //    else
    //    {
    //      PeersOutbound[indexPeer] = peerNew;
    //    }
    //  }

    //  Task startPeerTask = StartPeerAsync(peerNew);

    //  return peerNew;
    //}
    //bool IsPeerInbound(Peer peer)
    //{
    //  return PeersInbound.Contains(peer);
    //}

    public async Task<INetworkChannel> AcceptChannelInboundRequestAsync()
    {
      Peer peer;
      do
      {
        peer = await PeersRequestInbound.ReceiveAsync();
      } while (!peer.TryDispatch());

      return peer;
    }
        
    public async Task StartPeerInboundListenerAsync()
    {
      TcpListener.Start(PEERS_COUNT_INBOUND);

      while (true)
      {
        TcpClient client = await TcpListener.AcceptTcpClientAsync();
        Console.WriteLine("Received inbound request from " + client.Client.RemoteEndPoint.ToString());
        Peer peer = new Peer(client, this);
        PeersInbound.Add(peer);

        Task startPeerTask = peer.StartAsync();
      }
    }


    static long GetUnixTimeSeconds() => DateTimeOffset.UtcNow.ToUnixTimeSeconds();

    public async Task PingAsync()
    {
    }
    
    public uint GetProtocolVersion()
    {
      return ProtocolVersion;
    }
  }
}
