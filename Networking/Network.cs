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
    const uint MagicValue = 0xF9BEB4D9; // Bitcoin Main
    const uint MagicValueByteSize = 4;
    const UInt16 Port = 8333;
    const UInt32 ProtocolVersion = 70013;
    const ServiceFlags NetworkServicesRemoteRequired = ServiceFlags.NODE_NETWORK;
    const ServiceFlags NetworkServicesLocalProvided = ServiceFlags.NODE_NONE; 
    const string UserAgent = "/BToken:0.0.0/";
    const Byte RelayOption = 0x01;
    static readonly UInt64 Nonce = createNonce();

    List<Peer> PeerEndPoints = new List<Peer>();
    BufferBlock<NetworkMessage> UTXONetworkMessages = new BufferBlock<NetworkMessage>();
    BufferBlock<NetworkMessage> BlockchainNetworkMessages = new BufferBlock<NetworkMessage>();

    public Network()
    {
      // PeerEndPoint = new Peer(new IPEndPoint(IPAddress.Parse("185.6.124.16"), 8333), this);// Satoshi 0.16.0
      // PeerEndPoint = new Peer(new IPEndPoint(IPAddress.Parse("180.117.10.97"), 8333), this); // Satoshi 0.15.1
      // PeerEndPoint = new Peer(new IPEndPoint(IPAddress.Parse("49.64.118.12"), 8333), this); // Satoshi 0.15.1
      PeerEndPoints.Add(new Peer(new IPEndPoint(IPAddress.Parse("47.106.188.113"), 8333), this)); // Satoshi 0.16.0
      //PeerEndPoints.Add(new Peer(new IPEndPoint(IPAddress.Parse("172.116.169.85"), 8333), this)); // Satoshi 0.16.1 
    }

    public async Task startAsync(uint blockheightLocal)
    {
      try
      {
        await PeerEndPoints.First().startAsync(blockheightLocal);
      }
      catch (NetworkException ex)
      {
        throw new NetworkException("Connection failed with network", ex);
      }
    }

    async Task RelayMessageIncomingAsync(NetworkMessage networkMessage)
    {
      switch (networkMessage)
      {
        case InvMessage invMessage:
          await RelayInventoryMessageAsync(invMessage);
          break;
        case HeadersMessage headersMessage:
          await BlockchainNetworkMessages.SendAsync(headersMessage);
          break;
        default:
          break;
      }
    }
    async Task RelayInventoryMessageAsync(InvMessage invMessage)
    {
      if (invMessage.GetInventoriesBlock().Any())
      {
        await BlockchainNetworkMessages.SendAsync(invMessage);
      }
      if (invMessage.GetInventoriesTX().Any())
      {
        await UTXONetworkMessages.SendAsync(invMessage);
      }
    }

    public async Task<NetworkMessage> ReceiveBlockchainNetworkMessageAsync()
    {
      return await BlockchainNetworkMessages.ReceiveAsync();
    }
    public async Task<NetworkMessage> ReceiveUTXONetworkMessageAsync()
    {
      return await UTXONetworkMessages.ReceiveAsync();
    }

    public BufferBlock<NetworkBlock> GetBlocks(IEnumerable<UInt256> headerHashes)
    {
      return new BufferBlock<NetworkBlock>();
    }

    public async Task GetHeadersAsync(UInt256 headerHashChainTip)
    {
      await PeerEndPoints.First().GetHeadersAsync(new List<UInt256>() { headerHashChainTip });
    }
    public async Task GetHeadersAdvertisedAsync(InvMessage invMessage, IEnumerable<UInt256> headerLocator)
    {
      Peer peer = GetPeerOriginOfNetworkMessage(invMessage);
      if (peer == null)
      {
        Console.WriteLine("Could not find peer that advertised invMessage containing '{0}' block inventories.", invMessage.GetInventoriesBlock().Count);
        return;
      }
      await peer.GetHeadersAsync(headerLocator);
    }
    Peer GetPeerOriginOfNetworkMessage(NetworkMessage networkMessage)
    {
      return PeerEndPoints.Find(p => p.IsOriginOfNetworkMessage(networkMessage));
    }

    public async Task RequestOrphanParentHeadersAsync(HeadersMessage headersMessage, UInt256 hash, List<UInt256> headerLocator)
    {
      Peer peer = GetPeerOriginOfNetworkMessage(headersMessage);
      if (peer == null)
      {
        Console.WriteLine("Could not find peer that sent headersMessage containing orphan hash '{0}'", hash.ToString());
        return;
      }

      await peer.RequestOrphanParentHeadersAsync(headerLocator);
    }
    public void orphanBlockHash(UInt256 hash)
    {
      throw new NotImplementedException();
    }
    public void duplicateHash(HeadersMessage headersMessage, UInt256 hash)
    {
      Peer peer = GetPeerOriginOfNetworkMessage(headersMessage);
      if (peer == null)
      {
        Console.WriteLine("Could not find peer that sent headersMessage containing duplicate hash '{0}'", hash.ToString());
        return;
      }
    }
    public void invalidHash(UInt256 hash)
    {
      throw new NotImplementedException();
    }
    public void BlameMessage(NetworkMessage networkMessage, uint penaltyScore)
    {
      Peer peer = GetPeerOriginOfNetworkMessage(networkMessage);
      if (peer == null)
      {
        Console.WriteLine("Could not find peer that sent networkMessage which is to blame by '{0}' penalty score", penaltyScore);
        return;
      }

      peer.Blame(penaltyScore);
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

  }
}
