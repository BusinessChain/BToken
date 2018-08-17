﻿using System;
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
    const Byte RelayOption = 0x00;
    static readonly UInt64 Nonce = createNonce();

    List<Peer> PeersConnected = new List<Peer>();

    
    public Network()
    {
      // PeerEndPoint = new Peer(new IPEndPoint(IPAddress.Parse("185.6.124.16"), 8333), this);// Satoshi 0.16.0
      // PeerEndPoint = new Peer(new IPEndPoint(IPAddress.Parse("180.117.10.97"), 8333), this); // Satoshi 0.15.1
      //PeersConnected.Add(new Peer(new IPEndPoint(IPAddress.Parse("49.64.118.12"), 8333), this)); // Satoshi 0.16.0
      //PeersConnected.Add(new Peer(new IPEndPoint(IPAddress.Parse("47.106.188.113"), 8333), this)); // Satoshi 0.16.0
      PeersConnected.Add(new Peer(new IPEndPoint(IPAddress.Parse("172.116.169.85"), 8333), this)); // Satoshi 0.16.1 
    }

    public async Task startAsync(uint blockheightLocal)
    {
      try
      {
        await PeersConnected.First().startAsync(blockheightLocal);
      }
      catch (NetworkException ex)
      {
        throw new NetworkException("Connection failed with network", ex);
      }
    }
    
    public async Task<List<BufferBlock<NetworkMessage>>> GetNetworkBuffersBlockchainAsync()
    {
      List<Peer> connectedPeers = await GetConnectedPeersAsync();
      return connectedPeers.Select(p => p.NetworkMessageBufferBlockchain).ToList();
    }

    async Task<List<Peer>> GetConnectedPeersAsync()
    {
      if(PeersConnected.Count == 0)
      {
        Peer peerNew = await ConnectNewPeerAsync();
        PeersConnected.Add(peerNew);
      }

      return PeersConnected;
    }
    async Task<Peer> ConnectNewPeerAsync()
    {
      throw new NotImplementedException();
    }

    public BufferBlock<NetworkBlock> GetBlocks(IEnumerable<UInt256> headerHashes)
    {
      return new BufferBlock<NetworkBlock>();
    }

    public async Task GetHeadersAsync(UInt256 headerHashChainTip)
    {
      await PeersConnected.First().GetHeadersAsync(new List<UInt256>() { headerHashChainTip });
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
      return PeersConnected.Find(p => p.IsOwnerOfBuffer(buffer));
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
      PeersConnected.First().PingAsync();
    }
  }
}