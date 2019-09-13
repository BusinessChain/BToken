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
      Nonce = CreateNonce();
      AddressPool = new NetworkAddressPool();
      
      TcpListener = new TcpListener(IPAddress.Any, Port);
    }
    static ulong CreateNonce()
    {
      Random rnd = new Random();

      ulong number = (ulong)rnd.Next();
      number = number << 32;
      return number |= (uint)rnd.Next();
    }


    public void Start()
    {
      //Task peerInboundListenerTask = StartPeerInboundListenerAsync();
    }
    
    public async Task<INetworkChannel> RequestChannelAsync()
    {
      Peer peer = new Peer(this);

      while(!await peer.TryConnectAsync())
      {
        await Task.Delay(3000);
        peer = new Peer(this);
      }

      return peer;
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

        Task startPeerTask = peer.StartAsync();
      }
    }
    
    static long GetUnixTimeSeconds() => DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        
    public uint GetProtocolVersion()
    {
      return ProtocolVersion;
    }
  }
}
