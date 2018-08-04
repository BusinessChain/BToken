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
    private static readonly UInt64 Nonce = createNonce();
    const string UserAgent = "/BToken:0.0.0/";
    const Byte RelayOption = 0x01;

    List<Peer> PeerEndPoints = new List<Peer>();
    List<BufferBlock<NetworkMessage>> NetworkMessageListeners = new List<BufferBlock<NetworkMessage>>();

    public Network()
    {
      // PeerEndPoint = new Peer(new IPEndPoint(IPAddress.Parse("185.6.124.16"), 8333), this);// Satoshi 0.16.0
      // PeerEndPoint = new Peer(new IPEndPoint(IPAddress.Parse("180.117.10.97"), 8333), this); // Satoshi 0.15.1
      // PeerEndPoint = new Peer(new IPEndPoint(IPAddress.Parse("49.64.118.12"), 8333), this); // Satoshi 0.15.1
      // PeerEndPoint = new Peer(new IPEndPoint(IPAddress.Parse("47.106.188.113"), 8333), this); // Satoshi 0.16.0
      PeerEndPoints.Add(new Peer(new IPEndPoint(IPAddress.Parse("172.116.169.85"), 8333), this)); // Satoshi 0.16.1 
    }

    public async Task startAsync(uint blockheightLocal)
    {
      try
      {
        await PeerEndPoints.First().startAsync(blockheightLocal);
      }
      catch (NetworkProtocolException ex)
      {
        throw new NetworkProtocolException("Connection failed with network", ex);
      }
    }

    async Task ProcessMessageUnsolicitedAsync(NetworkMessage networkMessage)
    {
      foreach(BufferBlock<NetworkMessage> networkMessageListener in NetworkMessageListeners)
      {
        await networkMessageListener.SendAsync(networkMessage);
      }
    }

    public BufferBlock<NetworkMessage> GetNetworkMessageListener()
    {
      BufferBlock<NetworkMessage> networkMessageListener = new BufferBlock<NetworkMessage>();
      NetworkMessageListeners.Add(networkMessageListener);
      return networkMessageListener;
    }
    public void DisposeNetworkMessageListener(BufferBlock<NetworkMessage> networkMessageListener)
    {
      NetworkMessageListeners.Remove(networkMessageListener);
    }

    public BufferBlock<NetworkBlock> GetBlocks(IEnumerable<UInt256> headerHashes)
    {
      return new BufferBlock<NetworkBlock>();
    }

    public async Task GetHeadersAdvertisedAsync(InvMessage invMessage, UInt256 headerHashChainTip)
    {
      Peer peer = GetPeerOriginOfNetworkMessage(invMessage);
      if (peer == null)
      {
        return;
      }
      await peer.GetHeadersAdvertisedAsync(headerHashChainTip);
    }
    Peer GetPeerOriginOfNetworkMessage(NetworkMessage networkMessage)
    {
      return PeerEndPoints.Find(p => p.IsOriginOfNetworkMessage(networkMessage));
    }

    public void orphanHeaderHash(UInt256 hash)
    {
      // Gib Headers zurück um Root block zu suchen. Kann Peer nicht liefern, Kopf ab.
      Console.WriteLine("Received duplicate Block.");
    }
    public void orphanBlockHash(UInt256 hash)
    {
      throw new NotImplementedException();
    }
    public void duplicateHash(UInt256 hash)
    {
      Console.WriteLine("Received duplicate Block.");
    }
    public void invalidHash(UInt256 hash)
    {
      throw new NotImplementedException();
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
