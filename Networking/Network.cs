using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using System.Threading.Tasks.Dataflow;

namespace BToken.Networking
{
  partial class Network
  {
    const UInt16 Port = 8333;
    const UInt32 ProtocolVersion = 70013;
    const ServiceFlags NetworkServicesRemoteRequired = ServiceFlags.NODE_NETWORK;
    const ServiceFlags NetworkServicesLocalProvided = ServiceFlags.NODE_NONE; 
    const string UserAgent = "/BToken:0.0.0/";
    const Byte RelayOption = 0x00;
    static readonly UInt64 Nonce = createNonce();

    NetworkAddressPool AddressPool = new NetworkAddressPool();

    List<Peer> Peers = new List<Peer>();

    public Network()
    {
      //PeersConnected.Add(new Peer(new IPEndPoint(IPAddress.Parse("49.64.118.12"), 8333), this)); // Satoshi 0.16.0
      //PeersConnected.Add(new Peer(new IPEndPoint(IPAddress.Parse("47.106.188.113"), 8333), this)); // Satoshi 0.16.0
      //PeersConnected.Add(new Peer(new IPEndPoint(IPAddress.Parse("172.116.169.85"), 8333), this)); // Satoshi 0.16.1 
    }

    public async Task<BufferBlock<NetworkMessage>> CreateNetworkSessionBlockchainAsync(uint blockheightLocal)
    {
      Peer peer = CreatePeer();
      Peers.Add(peer);
      await peer.startAsync(blockheightLocal);
      return peer.NetworkMessageBufferBlockchain;
    }
    Peer CreatePeer()
    {
      IPAddress iPAddress = AddressPool.GetRandomNodeAddress();
      return new Peer(new IPEndPoint(iPAddress, Port), this);
    }

    public BufferBlock<NetworkBlock> GetBlocks(IEnumerable<UInt256> headerHashes)
    {
      return new BufferBlock<NetworkBlock>();
    }

    public async Task GetHeadersAsync(UInt256 headerHashChainTip)
    {
      Peers.ForEach(p => p.GetHeadersAsync(new List<UInt256>() { headerHashChainTip }));
    }
    public async Task GetHeadersAsync(BufferBlock<NetworkMessage> buffer, List<UInt256> headerLocator)
    {
      Peer peer = GetPeerOwnerOfBuffer(buffer);
      if (peer != null)
      {
        await peer.GetHeadersAsync(headerLocator);
      }
    }
    Peer GetPeerOwnerOfBuffer(BufferBlock<NetworkMessage> buffer)
    {
      return Peers.Find(p => p.IsOwnerOfBuffer(buffer));
    }

    public void BlameProtocolError(BufferBlock<NetworkMessage> buffer)
    {
      Peer peer = GetPeerOwnerOfBuffer(buffer);
      if (peer != null)
      {
        peer.Blame(20);
      }
    }
    public void BlameConsensusError(BufferBlock<NetworkMessage> buffer)
    {
      Peer peer = GetPeerOwnerOfBuffer(buffer);
      if (peer != null)
      {
        peer.Dispose();
      }
    }

    static UInt64 createNonce()
    {
      Random rnd = new Random();

      UInt64 number = (UInt64)rnd.Next();
      number = number << 32;
      return number |= (UInt32)rnd.Next();
    }
    public static long getUnixTimeSeconds()
    {
      return DateTimeOffset.UtcNow.ToUnixTimeSeconds();
    }

    public async Task PingAsync()
    {
      Peers.ForEach(p => p.PingAsync());
    }
  }
}
