using System.Diagnostics;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
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
    const ServiceFlags NetworkServicesLocalProvided = ServiceFlags.NODE_NONE; 
    const string UserAgent = "/BToken:0.0.0/";
    const Byte RelayOption = 0x00;
    static UInt64 Nonce;

    NetworkAddressPool AddressPool;

    List<Peer> Peers = new List<Peer>();

    public Network()
    {
      Nonce = createNonce();
      AddressPool = new NetworkAddressPool();
    }
    static ulong createNonce()
    {
      Random rnd = new Random();

      ulong number = (ulong)rnd.Next();
      number = number << 32;
      return number |= (uint)rnd.Next();
    }

    public async Task<BufferBlock<NetworkMessage>> CreateBlockchainChannelAsync(uint blockheightLocal)
    {
      try
      {
        Peer peer = CreatePeer();
        Peers.Add(peer);
        await peer.startAsync(blockheightLocal).ConfigureAwait(false);
        return peer.NetworkMessageBufferBlockchain;
      }
      catch (Exception ex)
      {
        return await CreateBlockchainChannelAsync(blockheightLocal).ConfigureAwait(false);
      }

    }
    Peer CreatePeer()
    {
      IPAddress iPAddress = AddressPool.GetRandomNodeAddress();
      return new Peer(new IPEndPoint(iPAddress, Port), this);
    }

    public void CloseChannel(BufferBlock<NetworkMessage> buffer)
    {
      Peer peer = GetPeerOwnerOfBuffer(buffer);

      if(peer != null)
      {
        peer.Dispose();
      }
    }

    public async Task GetHeadersAsync(List<UInt256> headerLocator) => Peers.ForEach(p => p.GetHeadersAsync(headerLocator));
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
    Peer GetPeerOwnerOfBuffer(BufferBlock<NetworkMessage> buffer) => Peers.Find(p => p.IsOwnerOfBuffer(buffer));

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

      foreach (Peer peer in Peers)
      {
        try
        {
          await peer.PingAsync();
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
      await Peers.First().GetBlocksAsync(blockHashes);
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
