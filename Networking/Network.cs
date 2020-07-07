﻿using System;
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
  public partial class Network
  {
    const UInt16 Port = 8333;
    public const UInt32 ProtocolVersion = 70015;
    const ServiceFlags NetworkServicesRemoteRequired = ServiceFlags.NODE_NETWORK;
    const ServiceFlags NetworkServicesLocalProvided = ServiceFlags.NODE_NETWORK;
    const string UserAgent = "/BToken:0.0.0/";
    const Byte RelayOption = 0x00;
    const int PEERS_COUNT_INBOUND = 8;

    static ulong Nonce = CreateNonce();

    NetworkAddressPool AddressPool;
    TcpListener TcpListener;

    readonly object LOCK_Peers = new object();
    List<Peer> Peers = new List<Peer>();

    BufferBlock<Peer> PeersRequest = new BufferBlock<Peer>();





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
      StartPeerInboundListener();
    }


    
    public async Task<BlockchainPeer> CreateNetworkPeer(
      Blockchain blockchain)
    {
      while (true)
      {
        IPAddress iPAddress;

        try
        {
          iPAddress = await GetNodeAddress();
        }
        catch
        {
          Console.WriteLine("Cannot create peer: No node address available.");
          Task.Delay(10000);
          continue;
        }

        Peer peer = new Peer(
          iPAddress,
          ConnectionType.OUTBOUND,
          this);

        try
        {
          await peer.Connect();
        }
        catch
        {
          peer.Dispose();

          Task.Delay(10000);
          continue;
        }

        peer.Run();

        return peer;
      }
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
