using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Threading.Tasks.Dataflow;

using BToken.Networking;

namespace BToken.Chaining
{
  public enum ChainLinkCode { ORPHAN, DUPLICATE, INVALID, EXPIRED, CHECKPOINT };


  partial class Blockchain
  {
    Network Network;
    BlockchainController Controller;

    static ChainBlock BlockGenesis;
    static UInt256 CheckpointHash;

    ChainSocket SocketMain;

    public Blockchain(ChainBlock blockGenesis, UInt256 checkpointHash, Network network)
    {
      Network = network;
      Controller = new BlockchainController(network, this);

      CheckpointHash = checkpointHash;
      BlockGenesis = blockGenesis;

      SocketMain = new ChainSocket(
        this,
        blockGenesis,
        0,
        0);
    }

    public async Task startAsync()
    {
      await Controller.StartAsync();
    }

    public async Task buildAsync()
    {
      //List<UInt256> headerLocator = getHeaderLocator();
      //BufferBlock<NetworkHeader> networkHeaderBuffer = new BufferBlock<NetworkHeader>();
      //Network.GetHeadersAsync(headerLocator);
      //await insertNetworkHeadersAsync(networkHeaderBuffer);
    }

    public List<UInt256> getBlockLocator()
    {
      uint getNextLocation(uint locator)
      {
        if (locator < 10)
          return locator + 1;
        else
          return locator * 2;
      }

      return getBlockLocator(CheckpointHash, getNextLocation);
    }
    List<UInt256> getBlockLocator(UInt256 checkpointHash, Func<uint, uint> getNextLocation)
    {
      List<UInt256> chainLinkLocator = new List<UInt256>();
      SocketMain.Probe.reset();
      uint locator = 0;

      while (true)
      {
        if (SocketMain.Probe.IsHash(checkpointHash) || SocketMain.Probe.IsGenesis())
        {
          chainLinkLocator.Add(SocketMain.Probe.getHash());
          return chainLinkLocator;
        }

        if (locator == SocketMain.Probe.Depth)
        {
          chainLinkLocator.Add(SocketMain.Probe.getHash());
          locator = getNextLocation(locator);
        }

        SocketMain.Probe.push();
      }

    }


    public ChainBlock GetChainBlock(UInt256 hash)
    {
      return GetSocketProbe(hash).Block;
    }
    ChainSocket.SocketProbe GetSocketProbe(UInt256 hash)
    {
      ChainSocket socket = SocketMain;
      resetProbes();

      while (socket != null)
      {
        if (socket.Probe.IsHash(hash))
        {
          return socket.Probe;
        }

        if (socket.Probe.IsGenesis())
        {
          socket.bypass();

          if (socket.isWeakerSocketProbeStrongerThan(socket.StrongerSocketActive))
          {
            socket = socket.WeakerSocketActive;
          }
          else
          {
            socket = socket.StrongerSocket;
          }
          continue;
        }

        if (socket.isProbeStrongerThan(socket.StrongerSocket))
        {
          if (socket.isWeakerSocketProbeStronger())
          {
            socket = socket.WeakerSocketActive;
          }

          socket.Probe.push();
        }
        else
        {
          socket = socket.StrongerSocket;
        }

      }

      return null;
    }
    void resetProbes()
    {
      ChainSocket socket = SocketMain;

      while (socket != null)
      {
        socket.reset();
        socket = socket.WeakerSocket;
      }
    }

    public async Task insertNetworkHeadersAsync(BufferBlock<NetworkHeader> headerBuffer)
    {
      NetworkHeader networkHeader = await headerBuffer.ReceiveAsync();

      while (networkHeader != null)
      {
        insertNetworkHeader(networkHeader);

        networkHeader = await headerBuffer.ReceiveAsync();
      }
    }
    public void insertNetworkHeader(NetworkHeader networkHeader)
    {
      UInt256 hash = calculateHash(networkHeader.getBytes());

      ChainBlock chainHeader = new ChainBlock(
        hash,
        networkHeader.HashPrevious,
        networkHeader.NBits,
        networkHeader.MerkleRootHash,
        networkHeader.UnixTimeSeconds
        );

      insertChainBlock(chainHeader);
    }

    static UInt256 calculateHash(byte[] headerBytes)
    {
      byte[] hashBytes = Hashing.sha256d(headerBytes);
      return new UInt256(hashBytes);
    }
    
    public void insertChainBlock(ChainBlock block)
    {
      if (IsTimestampExpired(block.UnixTimeSeconds))
      {
        throw new ChainLinkException(block, ChainLinkCode.EXPIRED);
      }

      ChainSocket.SocketProbe socketProbeHeaderPrevious = GetSocketProbe(block.HashPrevious);
      if (socketProbeHeaderPrevious == null)
      {
        throw new ChainLinkException(block, ChainLinkCode.ORPHAN);
      }

      ChainSocket socketNew = socketProbeHeaderPrevious.InsertBlock(block);
      InsertSocket(socketNew);
    }
    bool IsTimestampExpired(ulong unixTimeSeconds)
    {
      const long MAX_FUTURE_TIME_SECONDS = 2 * 60 * 60;
      return (long)unixTimeSeconds > (DateTimeOffset.UtcNow.ToUnixTimeSeconds() + MAX_FUTURE_TIME_SECONDS);
    }

    public uint getHeight()
    {
      return SocketMain.Height;
    }
    public UInt256 getHash()
    {
      return SocketMain.Block.Hash;
    }
        
    static ChainBlock GetBlockPrevious(ChainBlock block, uint depth)
    {
      if (depth > 0)
      {
        if (block == BlockGenesis)
        {
          throw new ArgumentOutOfRangeException("Genesis Block encountered prior specified depth has been reached.");
        }

        return GetBlockPrevious(block, --depth);
      }

      return block;
    }
    
    bool IsDeeperThanCheckpoint(ChainBlock block, UInt256 checkpointHash)
    {
      ChainSocket socket = SocketMain;
      socket.Probe.reset();

      while (!socket.Probe.IsGenesis())
      {
        if (socket.Probe.IsBlock(block))
        {
          return false;
        }

        if (socket.Probe.IsHash(checkpointHash))
        {
          return true;
        }

        socket.Probe.push();
      }

      throw new InvalidOperationException("Neither chainLink nor checkpoint in chain encountered.");
    }
    void ConnectChainLinks(ChainBlock blockPrevious, ChainBlock block)
    {
      block.BlockPrevious = blockPrevious;
      blockPrevious.BlocksNext.Add(block);
    }
    void InsertSocket(ChainSocket newSocket)
    {
      if (newSocket.isStrongerThan(SocketMain))
      {
        swapChain(newSocket, SocketMain);
        InsertSocket(newSocket);
      }

      ChainSocket socket = SocketMain;
      while (!newSocket.isStrongerThan(socket.WeakerSocket))
      {
        socket = socket.WeakerSocket;
      }

      socket.connectWeakerSocket(newSocket);
    }
    void swapChain(ChainSocket socket1, ChainSocket socket2)
    {
      ChainBlock chainLinkTemp = socket2.Block;
      socket2.AppendChainHeader(socket1.Block);
      socket1.AppendChainHeader(chainLinkTemp);
    }
  }
}
