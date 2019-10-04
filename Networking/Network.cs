using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Linq;
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
    const int PEERS_COUNT_OUTBOUND = 8;

    static UInt64 Nonce;

    NetworkAddressPool AddressPool;
    TcpListener TcpListener;
    
    List<Peer> PeersInbound = new List<Peer>();
    BufferBlock<Peer> PeersRequestInbound = new BufferBlock<Peer>();

    public int COUNT_CHANNELS_OUTBOUND { get; private set; }

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
      Parallel.For(
        0, COUNT_CHANNELS_OUTBOUND,
        i => CreateOutboundPeer());

      //Task peerInboundListenerTask = StartPeerInboundListenerAsync();
    }



    readonly object LOCK_ChannelsOutbound = new object();
    Queue<INetworkChannel> ChannelsOutbound = new Queue<INetworkChannel>();

    async Task CreateOutboundPeer()
    {
      var peer = new Peer(this);

      while(!await peer.TryConnect())
      {
        Console.WriteLine("Failed to connect to peer {0}", peer.GetIdentification());
        await Task.Delay(1000);
        peer = new Peer(this);
      }

      lock(LOCK_ChannelsOutbound)
      {
        ChannelsOutbound.Enqueue(peer);
      }

      Console.WriteLine("Created connection to peer {0}", peer.GetIdentification());
    }

    public async Task<INetworkChannel> RequestChannel()
    {
      do
      {
        lock (LOCK_ChannelsOutbound)
        {
          if (ChannelsOutbound.Any())
          {
            return ChannelsOutbound.Dequeue();
          }
        }

        await CreateOutboundPeer();
      } while (true);
    }

    public void ReturnChannel(INetworkChannel channel)
    {
      lock (LOCK_ChannelsOutbound)
      {
        ChannelsOutbound.Enqueue(channel);
      }
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

        peer.Start();
      }
    }
    
    static long GetUnixTimeSeconds() => DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        
    public uint GetProtocolVersion()
    {
      return ProtocolVersion;
    }
  }
}
