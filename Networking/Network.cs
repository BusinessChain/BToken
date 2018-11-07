using System.Diagnostics;

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
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
    
    static UInt64 Nonce;
    NetworkAddressPool AddressPool;

    TcpListener TcpListener;
    public const int PEERS_COUNT_INBOUND = 8;
    ConcurrentBag<Peer> PeersInbound = new ConcurrentBag<Peer>();

    public const int PEERS_COUNT_OUTBOUND = 8;
    ConcurrentBag<Peer> PeersOutbound = new ConcurrentBag<Peer>();

    BufferBlock<IPeerSession> PeerSessionQueue;
    BufferBlock<Peer> PeersAvailable;

    public Network()
    {
      Nonce = createNonce();
      AddressPool = new NetworkAddressPool();
      
      TcpListener = CreateTcpListener();

      CreatePeersOutbound();
    }
    static ulong createNonce()
    {
      Random rnd = new Random();

      ulong number = (ulong)rnd.Next();
      number = number << 32;
      return number |= (uint)rnd.Next();
    }
    static TcpListener CreateTcpListener()
    {
      try
      {
        return new TcpListener(IPAddress.Any, Port);
      }
      catch (Exception ex)
      {
        Debug.WriteLine(ex.ToString());
        return null;
      }
    }
    void CreatePeersOutbound()
    {
      Peer[] peers = new Peer[PEERS_COUNT_OUTBOUND];
      for (int i = 0; i < PEERS_COUNT_OUTBOUND; i++)
      {
        peers[i] = new Peer(this);
      }

      peers.Select(async peer =>
      {
        IPAddress iPAddress = AddressPool.GetRandomNodeAddress();
        await peer.ConnectAsync(iPAddress);
        Task peerStartTask = peer.ProcessNetworkMessageAsync();

        await PeersAvailable.SendAsync(peer);
      }).ToArray();
    }

    public void Start()
    {
      TcpListener.Start(PEERS_COUNT_INBOUND);
    }

    public async Task<BufferBlock<NetworkMessage>> AcceptInboundBlockchainChannelAsync(uint blockheightLocal)
    {
      while (true)
      {
        try
        {
          TcpClient client = await TcpListener.AcceptTcpClientAsync();
          Debug.WriteLine("received inbound request from " + client.Client.RemoteEndPoint.ToString());
          Peer peer = new Peer(client, this);

          await peer.HandshakeAsync(blockheightLocal).ConfigureAwait(false);

          PeersInbound.Add(peer);

          Task peerProcessNetworkMessageTask = peer.ProcessNetworkMessageAsync();

          return peer.NetworkMessageBufferBlockchain;
        }
        catch (Exception ex)
        {
          Debug.WriteLine(ex.Message);
        }
      }
    }

    public async Task<BufferBlock<NetworkMessage>> CreateBlockchainChannelAsync(uint blockheightLocal)
    {
      Peer peer = await CreatePeerAsync(blockheightLocal);
      return peer.NetworkMessageBufferBlockchain;
    }
    async Task<Peer> CreatePeerAsync(uint blockheightLocal)
    {
      int connectionTries = 0;
      while (true)
      {
        try
        {
          Peer peer = new Peer(this);

          IPAddress iPAddress = AddressPool.GetRandomNodeAddress();
          await peer.ConnectAsync(blockheightLocal, iPAddress);
          Task peerStartTask = peer.ProcessNetworkMessageAsync();

          PeersOutbound.Add(peer);

          return peer;
        }
        catch (Exception ex)
        {
          Debug.WriteLine("Network::CreateBlockchainChannel: " + ex.Message
            + "\nConnection tries: '{0}'", ++connectionTries);
        }
      }

    }

    public void CloseChannel(BufferBlock<NetworkMessage> buffer)
    {
      Peer peer = GetPeerOwnerOfBuffer(buffer);

      if(peer != null)
      {
        peer.Dispose();
      }
    }

    public async Task GetHeadersAsync(List<UInt256> headerLocator) => PeersOutbound.ForEach(p => p.GetHeadersAsync(headerLocator));
    public async Task GetHeadersAsync(BufferBlock<NetworkMessage> buffer, List<UInt256> headerLocator)
    {
      Peer peer = GetPeerOwnerOfBuffer(buffer);

      if (peer == null)
      {
        throw new NetworkException("No peer owning this buffer exists.");
      }

      try
      {
        await peer.GetHeadersAsync(headerLocator).ConfigureAwait(false);
      }
      catch
      {
        peer.Dispose();
        throw new NetworkException("Peer has been disposed.");
      }
    }
    Peer GetPeerOwnerOfBuffer(BufferBlock<NetworkMessage> buffer) => PeersOutbound.Find(p => p.IsOwnerOfBuffer(buffer));

    public void BlameProtocolError(BufferBlock<NetworkMessage> buffer)
    {
      Peer peer = GetPeerOwnerOfBuffer(buffer);
      if (peer == null)
      {
        throw new NetworkException("No peer owning this buffer exists.");
      }
      peer.Blame(20);
    }
    public void BlameConsensusError(BufferBlock<NetworkMessage> buffer)
    {
      Peer peer = GetPeerOwnerOfBuffer(buffer);
      if (peer == null)
      {
        throw new NetworkException("No peer owning this buffer exists.");
      }
      peer.Blame(100);
    }
    
    static long getUnixTimeSeconds() => DateTimeOffset.UtcNow.ToUnixTimeSeconds();

    public async Task PingAsync()
    {
      var faultedPeers = new List<Peer>();

      foreach (Peer peer in PeersOutbound)
      {
        try
        {
          await peer.PingAsync().ConfigureAwait(false);
        }
        catch
        {
          faultedPeers.Add(peer);
        }
      }

      faultedPeers.ForEach(p => p.Dispose());
    }

    public async Task GetBlocksAsync(List<UInt256> blockHashes)
    {
      await PeersOutbound.First().GetBlocksAsync(blockHashes).ConfigureAwait(false);
    }
    public async Task GetBlockAsync(BufferBlock<NetworkMessage> buffer, List<UInt256> blockHashes)
    {
      Peer peer = GetPeerOwnerOfBuffer(buffer);

      if (peer == null)
      {
        throw new NetworkException("No peer owning this buffer exists.");
      }

      try
      {
        await peer.GetBlocksAsync(blockHashes).ConfigureAwait(false);
      }
      catch(Exception ex)
      {
        peer.Dispose();
        Debug.WriteLine(ex.Message);
        throw new NetworkException("Peer discarded due to connection error.");
      }
    }

  }
}
