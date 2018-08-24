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
    static UInt64 Nonce;

    NetworkAddressPool AddressPool;

    List<Peer> Peers = new List<Peer>();

    public Network()
    {
      Nonce = createNonce();
      AddressPool = new NetworkAddressPool();
    }

    public async Task<BufferBlock<NetworkMessage>> CreateBlockchainSessionAsync(uint blockheightLocal)
    {
      try
      {
        Peer peer = CreatePeer();
        Peers.Add(peer);
        await peer.startAsync(blockheightLocal);
        return peer.NetworkMessageBufferBlockchain;
      }
      catch (Exception ex)
      {
        Console.WriteLine(ex.Message);
        return await CreateBlockchainSessionAsync(blockheightLocal);
      }

    }
    Peer CreatePeer()
    {
      IPAddress iPAddress = AddressPool.GetRandomNodeAddress();
      return new Peer(new IPEndPoint(iPAddress, Port), this);
    }

    public void DisposeSession(BufferBlock<NetworkMessage> buffer)
    {
      try
      {
        Peer peer = GetPeerOwnerOfBuffer(buffer);
        peer.Dispose();
      }
      catch
      {
        return;
      }
    }
    
    public async Task GetHeadersAsync(List<UInt256> headerLocator)
    {
      Peers.ForEach(p => p.GetHeadersAsync(headerLocator));
    }
    public async Task GetHeadersAsync(BufferBlock<NetworkMessage> buffer, List<UInt256> headerLocator)
    {
      Peer peer = GetPeerOwnerOfBuffer(buffer);

      try
      {
        await peer.GetHeadersAsync(headerLocator);
      }
      catch
      {
        peer.Dispose();
        throw new NetworkException("Peer discarded due to connection error.");
      }
    }
    Peer GetPeerOwnerOfBuffer(BufferBlock<NetworkMessage> buffer)
    {
      Peer peer = Peers.Find(p => p.IsOwnerOfBuffer(buffer));

      if (peer == null)
      {
        throw new NetworkException("No peer owning this buffer.");
      }

      return peer;
    }

    public void BlameProtocolError(BufferBlock<NetworkMessage> buffer)
    {
      Peer peer = GetPeerOwnerOfBuffer(buffer);
      peer.Blame(20);
    }
    public void BlameConsensusError(BufferBlock<NetworkMessage> buffer)
    {
      Peer peer = GetPeerOwnerOfBuffer(buffer);
      peer.Blame(100);
    }

    static UInt64 createNonce()
    {
      Random rnd = new Random();

      UInt64 number = (UInt64)rnd.Next();
      number = number << 32;
      return number |= (UInt32)rnd.Next();
    }
    static long getUnixTimeSeconds()
    {
      return DateTimeOffset.UtcNow.ToUnixTimeSeconds();
    }

    public async Task PingAsync()
    {
      var faultedPeers = new List<Peer>();

      foreach(Peer peer in Peers)
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

  }
}
