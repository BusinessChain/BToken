using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Linq;
using System.Threading.Tasks;
using System.Threading.Tasks.Dataflow;


namespace BToken.Networking
{
  partial class Network
  {
    const UInt16 Port = 8333;
    const UInt32 ProtocolVersion = 70015;
    const ServiceFlags NetworkServicesRemoteRequired = ServiceFlags.NODE_NETWORK;
    const ServiceFlags NetworkServicesLocalProvided = ServiceFlags.NODE_NETWORK;
    const string UserAgent = "/BToken:0.0.0/";
    const Byte RelayOption = 0x00;
    const int PEERS_COUNT_INBOUND = 8;
    const int PEERS_COUNT_OUTBOUND = 4;

    static UInt64 Nonce = CreateNonce();

    NetworkAddressPool AddressPool;
    TcpListener TcpListener;

    readonly object LOCK_ChannelsInbound = new object();
    List<INetworkChannel> ChannelsInbound = new List<INetworkChannel>();

    BufferBlock<Peer> PeersRequestInbound = new BufferBlock<Peer>();
    

    public Network()
    {
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
        0, PEERS_COUNT_OUTBOUND,
        i => CreateOutboundPeer());

      StartPeerInboundListener();
    }



    readonly object LOCK_ChannelsOutbound = new object();
    List<INetworkChannel> ChannelsOutbound = new List<INetworkChannel>();

    async Task CreateOutboundPeer()
    {
      var peer = new Peer(
        ConnectionType.OUTBOUND,
        this);

      while(!await peer.TryConnect())
      {
        Console.WriteLine("failed to created peer {0}", peer.GetIdentification());

        await Task.Delay(1000);

        peer = new Peer(
          ConnectionType.OUTBOUND, 
          this);
      }

      lock(LOCK_ChannelsOutbound)
      {
        ChannelsOutbound.Add(peer);
      }

      Console.WriteLine("created peer {0}", peer.GetIdentification());
    }

    readonly object LOCK_IsAddressPoolLocked = new object();
    bool IsAddressPoolLocked;
    async Task<IPAddress> GetNodeAddress()
    {
      while (true)
      {
        lock (LOCK_IsAddressPoolLocked)
        {
          if (!IsAddressPoolLocked)
          {
            IsAddressPoolLocked = true;
            break;
          }
        }

        await Task.Delay(1000);
      } 

      IPAddress iPAddress = AddressPool.GetNodeAddress();

      lock (LOCK_IsAddressPoolLocked)
      {
        IsAddressPoolLocked = false;
      }

      return iPAddress;
    }

    public async Task<INetworkChannel> RequestChannel()
    {
      do
      {
        lock (LOCK_ChannelsOutbound)
        {
          if (ChannelsOutbound.Any())
          {
            var channel = ChannelsOutbound.First();
            ChannelsOutbound.RemoveAt(0);

            return channel;
          }
        }

        await Task.Delay(1000);

      } while (true);
    }

    public void DisposeChannel(INetworkChannel channel)
    {
      channel.Dispose();
      CreateOutboundPeer();
    }

    public async Task<INetworkChannel> AcceptChannelInboundRequestAsync()
    {
      return await PeersRequestInbound.ReceiveAsync();
    }
        
    public async Task StartPeerInboundListener()
    {
      TcpListener.Start(PEERS_COUNT_INBOUND);

      while (true)
      {
        TcpClient client = await TcpListener.AcceptTcpClientAsync().
          ConfigureAwait(false);

        Console.WriteLine("Received inbound request from {0}",
          client.Client.RemoteEndPoint.ToString());

        Peer peer = new Peer(
          client, 
          ConnectionType.INBOUND,
          this);

        peer.Start();

        lock (LOCK_ChannelsInbound)
        {
          ChannelsInbound.Add(peer);
        }
      }
    }
    
    static long GetUnixTimeSeconds() => DateTimeOffset.UtcNow.ToUnixTimeSeconds();
       
    public void SendToInbound(NetworkMessage message)
    {
      ChannelsInbound.ForEach(c => c.SendMessage(message));
    }
  }
}
