using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Threading.Tasks.Dataflow;

using BToken.Chaining;

namespace BToken.Networking
{
  partial class Network
  {
    const UInt16 Port = 8333;
    public const UInt32 ProtocolVersion = 70015;
    const ServiceFlags NetworkServicesRemoteRequired = ServiceFlags.NODE_NETWORK;
    const ServiceFlags NetworkServicesLocalProvided = ServiceFlags.NODE_NETWORK;
    const string UserAgent = "/BToken:0.0.0/";
    const Byte RelayOption = 0x00;
    const int PEERS_COUNT_INBOUND = 8;
    public const int COUNT_PEERS_OUTBOUND = 4;

    static ulong Nonce = CreateNonce();

    NetworkAddressPool AddressPool;
    TcpListener TcpListener;

    readonly object LOCK_Peers = new object();
    List<Peer> Peers = new List<Peer>();

    BufferBlock<Peer> PeersRequest = new BufferBlock<Peer>();


    public Blockchain Blockchain;


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
      // Mache das im Blockchain Objekt. Sobald mit einem Peer verbunden
      // werden kann machen starten wir die Blockchain-Synchronisierung
      Parallel.For(
        0, COUNT_PEERS_OUTBOUND,
        i => RunOutboundPeer());

      StartPeerInboundListener();
    }



    async Task RunOutboundPeer()
    {
      while (true)
      {
        Peer peer;

        try
        {
          IPAddress iPAddress = await GetNodeAddress();
          var iPEndPoint = new IPEndPoint(iPAddress, Port);

          peer = new Peer(
            iPEndPoint,
            ConnectionType.OUTBOUND,
            this);
        }
        catch(Exception ex)
        {
          Console.WriteLine(
            "{0} when creating outbound peer: {1}",
            ex.GetType(),
            ex.Message);

          Task.Delay(10000);
          continue;
        }

        lock (LOCK_Peers)
        {
          Peers.Add(peer);

          Console.WriteLine(
            "Created peer {0}, total {1} peers created.",
            peer.IPEndPoint,
            Peers.Count);
        }

        try
        {
          await peer.Connect();
          await peer.Run();
        }
        catch(Exception ex)
        {
          peer.Dispose();

          lock (LOCK_Peers)
          {
            Peers.Remove(peer);

            Console.WriteLine(
              "{0} with peer {1}: {2}\n" +
              "total peers {3}",
              ex.GetType(),
              peer.GetIdentification(),
              ex.Message,
              Peers.Count);
          }
        }
      }
    }

    public async Task<INetworkChannel> CreateNetworkPeer()
    {

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

         
    public async Task<INetworkChannel> DispatchPeerOutbound(
      CancellationToken cancellationToken)
    {
      do
      {
        lock (LOCK_Peers)
        {
          Peer peer = Peers.Find(p =>
            p.ConnectionType == ConnectionType.OUTBOUND &&
            p.TryDispatch());

          if(peer != null)
          {
            return peer;
          }
        }

        Console.WriteLine("waiting for channel to dispatch.");

        await Task.Delay(1000, cancellationToken)
          .ConfigureAwait(false);

      } while (true);
    }

    public async Task<INetworkChannel> AcceptChannelRequest()
    {
      return await PeersRequest.ReceiveAsync();
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

        lock (LOCK_Peers)
        {
          Peers.Add(peer);
        }

        peer.Run();
      }
    }

  }
}
