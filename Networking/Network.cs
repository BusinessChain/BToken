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
    List<Peer> PeersOutbound = new List<Peer>();
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
        RemoveChannel(peer);
      }
    }
    public void RemoveChannel(INetworkChannel channel)
    {
      Peer peer = (Peer)channel;

      if (IsPeerInbound(peer))
      {
        PeersInbound.Remove(peer);
      }
      else
      {
        ReplacePeerOutbound(peer);
      }
    }
    void ReplacePeerOutbound(Peer peer)
    {
      var peerNew = new Peer(this);

      lock(ListPeersOutboundLOCK)
      {
        int indexPeer = PeersOutbound.FindIndex(p => p == peer);
        PeersOutbound[indexPeer] = peerNew;
      }

      Task startPeerTask = StartPeerAsync(peerNew);
    }
    bool IsPeerInbound(Peer peer)
    {
      return PeersInbound.Contains(peer);
    }

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

        Task startPeerTask = StartPeerAsync(peer);
      }
    }

    public async Task ExecuteSessionAsync(INetworkSession session)
    {
      while (true)
      {
        using (Peer peer = await DispatchPeerOutboundAsync(default(CancellationToken)))
        {
          if (await peer.TryExecuteSessionAsync(session, default(CancellationToken)))
          {
            return;
          }

          ReplacePeerOutbound(peer);
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

        // add additional peer if bottleneck here, e.g. if 3 times no dispatch then create new peer
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
