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
  public partial class Network : Chaining.INetwork, Accounting.INetwork
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


    readonly object ListPeersLOCK = new object();
    List<Peer> PeersOutbound = new List<Peer>();
    List<Peer> PeersInbound = new List<Peer>();

    BufferBlock<Peer> PeerRequestInboundBuffer = new BufferBlock<Peer>();


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
        var peer = new Peer(this);
        PeersOutbound.Add(peer);
      }
    }

    public void Start()
    {
      StartPeers();

      Task peerInboundListenerTask = StartPeerInboundListenerAsync();
    }
    void StartPeers()
    {
      PeersOutbound.Select(async peer =>
      {
        Task startPeerTask = StartPeerAsync(peer);
      }).ToArray();
    }
    async Task StartPeerAsync(Peer peer)
    {
      try
      {
        await peer.StartAsync();
      }
      catch
      {
        RenewPeerWhenOutbound(peer);
      }
    }
    void RenewPeerWhenOutbound(Peer peer)
    {
      if (!PeersOutbound.Contains(peer))
      {
        return;
      }

      var peerNew = new Peer(this);

      lock(ListPeersLOCK)
      {
        int indexPeer = PeersOutbound.FindIndex(p => p == peer);
        PeersOutbound[indexPeer] = peerNew;
      }

      Task startPeerTask = StartPeerAsync(peerNew);
    }

    public async Task<INetworkChannel> AcceptChannelInboundSessionRequestAsync()
    {
      return await PeerRequestInboundBuffer.ReceiveAsync();
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

        Task startPeerTask = StartPeerAsync(peer);
      }
    }

    public async Task ExecuteSessionAsync(INetworkSession session)
    {
      while (true)
      {
        using (Peer peer = await DispatchPeerOutboundAsync(default(CancellationToken)))
        {
          if (await peer.TryExecuteSessionAsync(session, default(CancellationToken))) { break; }
        }
      }
    }
    async Task<Peer> DispatchPeerOutboundAsync(CancellationToken cancellationToken)
    {
      while (true)
      {
        foreach (Peer peer in PeersOutbound)
        {
          if (peer.TryDispatch())
          {
            return peer;
          }
        }
        
        await Task.Delay(1000, cancellationToken).ConfigureAwait(false);
      }
    }

    static long GetUnixTimeSeconds() => DateTimeOffset.UtcNow.ToUnixTimeSeconds();

    public async Task PingAsync()
    {
      foreach (Peer peer in PeersOutbound)
      {
        await peer.PingAsync().ConfigureAwait(false);
      }
    }
    
    public uint GetProtocolVersion()
    {
      return ProtocolVersion;
    }
  }
}
