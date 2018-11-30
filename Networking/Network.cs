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


namespace BToken.Networking
{
  public partial class Network : Chaining.INetwork
  {
    const UInt16 Port = 8333;
    const UInt32 ProtocolVersion = 70013;
    const ServiceFlags NetworkServicesRemoteRequired = ServiceFlags.NODE_NETWORK;
    const ServiceFlags NetworkServicesLocalProvided = ServiceFlags.NODE_NETWORK;
    const string UserAgent = "/BToken:0.0.0/";
    const Byte RelayOption = 0x00;
    const int PEERS_COUNT_OUTBOUND = 1;
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

    public async Task ExecuteSessionAsync(INetworkSession session, CancellationToken cancellationToken)
    {
      Peer peer = await GetPeerOutboundAsync(cancellationToken);

      while(!await TryExecuteSessionAsync(session, peer, cancellationToken))
      {
        peer = await GetPeerOutboundAsync(cancellationToken);
      }
    }
    async Task<Peer> GetPeerOutboundAsync(CancellationToken cancellationToken)
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
    public async Task<bool> TryExecuteSessionAsync(INetworkSession session, INetworkChannel channel, CancellationToken cancellationToken)
    {
      Peer peer = (Peer)channel;

      try
      {
        await session.RunAsync(peer, cancellationToken);
        peer.ReportSessionSuccess(session);

        return true;
      }
      catch (Exception ex)
      {
        Console.WriteLine("Session '{0}' with peer '{1}' ended with exception: \n'{2}'",
          session.GetType().ToString(),
          peer.IPEndPoint.Address.ToString(),
          ex.Message);

        peer.ReportSessionFail(session);

        return false;
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

    public async Task<NetworkBlock> GetBlockAsync(UInt256 blockHash)
    {
      CancellationToken cancellationToken = new CancellationTokenSource(TimeSpan.FromSeconds(10)).Token;
      return await PeersOutbound.First().GetBlockAsync(blockHash, cancellationToken).ConfigureAwait(false);
    }

  }
}
