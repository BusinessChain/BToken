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

    Peer PeerEndPoint;
    List<BufferBlock<NetworkMessage>> NetworkMessageListeners = new List<BufferBlock<NetworkMessage>>();


    public Network()
    {
      // PeerEndPoint = new Peer(new IPEndPoint(IPAddress.Parse("185.6.124.16"), 8333), this);// Satoshi 0.16.0
      //PeerEndPoint = new Peer(new IPEndPoint(IPAddress.Parse("180.117.10.97"), 8333), this); // Satoshi 0.15.1
      // PeerEndPoint = new Peer(new IPEndPoint(IPAddress.Parse("49.64.118.12"), 8333), this); // Satoshi 0.15.1
      //PeerEndPoint = new Peer(new IPEndPoint(IPAddress.Parse("47.106.188.113"), 8333), this); // Satoshi 0.16.0
      PeerEndPoint = new Peer(new IPEndPoint(IPAddress.Parse("172.116.169.85"), 8333), this); // Satoshi 0.16.1 
    }

    public async Task startAsync(uint blockheightLocal)
    {
      try
      {
        await PeerEndPoint.startAsync(blockheightLocal);
      }
      catch (NetworkProtocolException ex)
      {
        throw new NetworkProtocolException("Connection failed with network", ex);
      }
    }

    async Task WriteMessageToListeners(NetworkMessage networkMessage)
    {
      NetworkMessage networkMessageCasted = CastNetworkMessage(networkMessage);
      foreach(BufferBlock<NetworkMessage> networkMessageListener in NetworkMessageListeners)
      {
        await networkMessageListener.SendAsync(networkMessageCasted);
      }
    }
    NetworkMessage CastNetworkMessage(NetworkMessage networkMessage)
    {
      switch (networkMessage.Command)
      {
        case "version":
          return new VersionMessage(networkMessage.Payload);
        case "verack":
          return new VerAckMessage();
        case "reject":
          return new RejectMessage(networkMessage.Payload);
        case "inv":
          return new InvMessage(networkMessage.Payload);
        case "headers":
          return new HeadersMessage(networkMessage.Payload);
        default:
          throw new NetworkProtocolException(string.Format("Peer sent unknown NetworkMessage"));
      }
    }

    public BufferBlock<NetworkMessage> GetNetworkMessageListener()
    {
      BufferBlock<NetworkMessage> networkMessageListener = new BufferBlock<NetworkMessage>(
        new DataflowBlockOptions
        {
          BoundedCapacity = 100
        });
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
    
    public BufferBlock<NetworkHeader> GetHeaders(IEnumerable<UInt256> headerLocator)
    {
      BufferBlock<NetworkHeader> headerBuffer = new BufferBlock<NetworkHeader>();
      Peer peer = getPeerHighestChain();
      Task getHeadersTask = peer.GetHeadersAsync(headerLocator, headerBuffer);
      return headerBuffer;
    }
    Peer getPeerHighestChain()
    {
      //return PeersConnected.OrderBy(p => p.getChainHeight()).Last();
      return PeerEndPoint;
    }

    public void orphanHeaderHash(UInt256 hash)
    {
      // Gib Headers zurück um Root block zu suchen. Kann Peer nicht liefern, Kopf ab.
      throw new NotImplementedException();
    }
    public void orphanBlockHash(UInt256 hash)
    {
      throw new NotImplementedException();
    }
    public void duplicateHash(UInt256 hash)
    {
      throw new NotImplementedException();
    }
    public void invalidHash(UInt256 hash)
    {
      throw new NotImplementedException();
    }

    public async Task<NetworkMessage> readMessageAsync()
    {
      CancellationTokenSource cts = new CancellationTokenSource();
      List<Task<NetworkMessage>> readPeerMessageTasks = new List<Task<NetworkMessage>>();

      Task<NetworkMessage> readPeerMessageTask = null;//PeerEndPoint.ReadMessageAsync();

      if (readPeerMessageTask.Status == TaskStatus.RanToCompletion)
      {
        cts.Cancel();
        return readPeerMessageTask.Result;
      }

      Task<NetworkMessage> readFirstPeerMessageTask = await Task.WhenAny(readPeerMessageTasks).ConfigureAwait(false);
      cts.Cancel();
      return readFirstPeerMessageTask.Result;

      // Todo: Exception handling when readFirstPeerMessageTask failed.
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
