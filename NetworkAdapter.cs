using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using System.Threading.Tasks.Dataflow;

namespace BToken.Networking
{
  partial class NetworkAdapter
  {
    const uint MagicValue = 0xF9BEB4D9; // Bitcoin Main
    const uint MagicValueByteSize = 4;
    const UInt16 PortLocal = 8333;
    const UInt32 ProtocolVersion = 70013;
    const ServiceFlags NetworkServicesRemoteRequired = ServiceFlags.NODE_NETWORK;
    const ServiceFlags NetworkServicesLocalProvided = ServiceFlags.NODE_NONE; 
    private static readonly IPAddress IPAddressLocal = IPAddress.Loopback.MapToIPv6();
    private static readonly UInt64 Nonce = createNonce();
    const string UserAgent = "/bToken:0.0.0/";
    const Byte RelayOption = 0x01;

    List<IPEndPoint> PeerIPEndPoints = new List<IPEndPoint>();
    List<Peer> PeerEndPoints = new List<Peer>();
    List<Peer> PeersConnected = new List<Peer>();


    public NetworkAdapter()
    {
      generatePeerEndPoints();
    }
    void generatePeerEndPoints()
    {
      foreach (IPEndPoint peerIPEndPoint in PeerIPEndPoints)
      {
        Peer peer = new Peer(peerIPEndPoint, this);
        PeerEndPoints.Add(peer);
      }
    }

    public async Task startAsync(uint blockheightLocal)
    {
      IEnumerable<Task> startPeerTasksQuery = from p in PeerEndPoints select p.startAsync(blockheightLocal);
      List<Task> startPeerTasks = startPeerTasksQuery.ToList();
      Task startFirstPeerAsyncTask = await Task.WhenAny(startPeerTasks);
      await startFirstPeerAsyncTask;
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
      return PeersConnected.OrderBy(p => p.getChainHeight()).Last();
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

      foreach (Peer peerConnected in PeersConnected)
      {
        Task<NetworkMessage> readPeerMessageTask = peerConnected.readMessageAsync(cts.Token);

        if (readPeerMessageTask.Status == TaskStatus.RanToCompletion)
        {
          cts.Cancel();
          return readPeerMessageTask.Result;
        }
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
