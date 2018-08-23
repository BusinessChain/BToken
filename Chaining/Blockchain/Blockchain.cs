using System;
using System.Linq;
using System.Collections.Generic;
using System.Threading.Tasks;

using BToken.Networking;


namespace BToken.Chaining
{
  public enum BlockCode { ORPHAN, DUPLICATE, INVALID, EXPIRED, CHECKPOINT };


  partial class Blockchain
  {
    Network Network;
    BlockchainController Controller;

    static ChainBlock BlockGenesis;
    static BlockLocation Checkpoint;

    ChainSocket SocketMain;

    public Blockchain(ChainBlock blockGenesis, BlockLocation checkpoint, Network network)
    {
      Network = network;
      Controller = new BlockchainController(network, this);

      Checkpoint = checkpoint;
      BlockGenesis = blockGenesis;

      SocketMain = new ChainSocket(
        blockchain: this,
        blockGenesis: blockGenesis,
        hash: CalculateHash(blockGenesis.Header.getBytes()),
        accumulatedDifficultyPrevious: 0,
        height: 0);
    }
    static UInt256 CalculateHash(byte[] headerBytes)
    {
      byte[] hashBytes = Hashing.sha256d(headerBytes);
      return new UInt256(hashBytes);
    }

    public async Task startAsync()
    {
      await Controller.StartAsync();
    }


    List<UInt256> GetBlockLocator()
    {
      return SocketMain.GetBlockLocator().Select(b => b.Hash).ToList();
    }

    ChainBlock GetBlock(UInt256 hash)
    {
      ChainSocket socket = GetSocketWithProbeAtBlock(hash);

      if(socket == null)
      {
        return null;
      }

      return socket.Probe.Block;
    }

    ChainSocket GetSocketWithProbeAtBlock(UInt256 hash)
    {
      ChainSocket socket = SocketMain;
      resetProbes();

      while (socket != null)
      {
        if (socket.Probe.IsHash(hash))
        {
          return socket;
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

    void insertHeader(NetworkHeader header)
    {
      if (IsTimestampExpired(header.UnixTimeSeconds))
      {
        throw new BlockchainException(BlockCode.EXPIRED);
      }

      ChainSocket socket = GetSocketWithProbeAtBlock(header.HashPrevious);

      if (socket == null)
      {
        throw new BlockchainException(BlockCode.ORPHAN);
      }

      UInt256 headerHash = CalculateHash(header.getBytes());
      ChainBlock block = socket.InsertHeader(header, headerHash);

      if(socket.Probe.Depth == 0)
      {
        socket.ConnectNextBlock(block, headerHash);

        if(socket == SocketMain)
        {
          return;
        }
        else
        {
          disconnectSocket(socket);
        }
      }
      else
      {
        socket = new ChainSocket(
          this,
          block,
          headerHash,
          socket.Probe.AccumulatedDifficulty,
          socket.Probe.GetHeight() + 1);
      }
      
      InsertSocket(socket);
    }
    void disconnectSocket(ChainSocket socket)
    {
      if (socket.StrongerSocket != null)
      {
        socket.StrongerSocket.WeakerSocket = socket.WeakerSocket;
        socket.StrongerSocket.WeakerSocketActive = socket.WeakerSocket;
      }
      if (socket.WeakerSocket != null)
      {
        socket.WeakerSocket.StrongerSocket = socket.StrongerSocket;
        socket.WeakerSocket.StrongerSocketActive = socket.StrongerSocket;
      }

      socket.StrongerSocket = null;
      socket.StrongerSocketActive = null;
      socket.WeakerSocket = null;
      socket.WeakerSocketActive = null;
    }

    bool IsTimestampExpired(ulong unixTimeSeconds)
    {
      const long MAX_FUTURE_TIME_SECONDS = 2 * 60 * 60;
      return (long)unixTimeSeconds > (DateTimeOffset.UtcNow.ToUnixTimeSeconds() + MAX_FUTURE_TIME_SECONDS);
    }

    uint GetHeight()
    {
      return SocketMain.Height;
    }
        
    static ChainBlock GetBlockPrevious(ChainBlock block, uint depth)
    {
      if (depth > 0)
      {
        if (block == BlockGenesis)
        {
          throw new ArgumentOutOfRangeException("Genesis Block encountered prior specified depth has been reached.");
        }

        return GetBlockPrevious(block.BlockPrevious, --depth);
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
    void InsertSocket(ChainSocket newSocket)
    {
      if (newSocket.isStrongerThan(SocketMain))
      {
        newSocket.connectWeakerSocket(SocketMain);
        SocketMain = newSocket;
      }

      ChainSocket socket = SocketMain;
      while (!newSocket.isStrongerThan(socket.WeakerSocket))
      {
        socket = socket.WeakerSocket;
      }

      socket.connectWeakerSocket(newSocket);
    }
  }
}
