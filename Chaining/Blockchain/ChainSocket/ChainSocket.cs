using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace BToken.Chaining
{
  partial class Blockchain
  {
    partial class ChainSocket
    {
      Blockchain Blockchain;

      ChainBlock Block;
      public UInt256 Hash;
      BlockLocator Locator;

      double AccumulatedDifficulty;
      public uint Height;

      public ChainSocket StrongerSocket;
      public ChainSocket StrongerSocketActive;
      public ChainSocket WeakerSocket;
      public ChainSocket WeakerSocketActive;

      public SocketProbe Probe;


      public ChainSocket
        (
        Blockchain blockchain,
        ChainBlock block,
        UInt256 hash,
        double accumulatedDifficultyPrevious,
        uint height
        )
      {
        Blockchain = blockchain;
        Probe = new SocketProbe(this);

        Hash = hash;
        Block = block;
        Locator = new BlockLocator(this);

        AccumulatedDifficulty = accumulatedDifficultyPrevious + TargetManager.GetDifficulty(block.Header.NBits);
        Height = height;

      }

      public bool IsWeakerSocketProbeStrongerThan(ChainSocket socket)
      {
        return WeakerSocketActive != null && WeakerSocketActive.isProbeStrongerThan(socket);
      }
      public bool isWeakerSocketProbeStronger()
      {
        return WeakerSocketActive != null && WeakerSocketActive.isProbeStrongerThan(this);
      }
      public void bypass()
      {
        if (StrongerSocketActive != null)
        {
          StrongerSocketActive.WeakerSocketActive = WeakerSocketActive;
        }
        if (WeakerSocketActive != null)
        {
          WeakerSocketActive.StrongerSocketActive = StrongerSocketActive;
        }
      }
      public void reset()
      {
        Probe.reset();

        StrongerSocketActive = StrongerSocket;
        WeakerSocketActive = WeakerSocket;
      }

      public void connectWeakerSocket(ChainSocket weakerSocket)
      {
        weakerSocket.WeakerSocket = WeakerSocket;
        weakerSocket.WeakerSocketActive = WeakerSocket;

        weakerSocket.StrongerSocket = this;
        weakerSocket.StrongerSocketActive = this;

        if (WeakerSocket != null)
        {
          WeakerSocket.StrongerSocket = weakerSocket;
          WeakerSocket.StrongerSocketActive = weakerSocket;
        }

        WeakerSocket = weakerSocket;
        WeakerSocketActive = weakerSocket;
      }
      
      void ConnectNextBlock(ChainBlock block, UInt256 headerHash)
      {
        Block = block;
        Hash = headerHash;
        AccumulatedDifficulty += TargetManager.GetDifficulty(block.Header.NBits);
        Height++;

        Locator.Update(Height, Hash);
      }

      void Disconnect()
      {
        if (StrongerSocket != null)
        {
          StrongerSocket.WeakerSocket = WeakerSocket;
          StrongerSocket.WeakerSocketActive = WeakerSocket;
        }
        if (WeakerSocket != null)
        {
          WeakerSocket.StrongerSocket = StrongerSocket;
          WeakerSocket.StrongerSocketActive = StrongerSocket;
        }

        StrongerSocket = null;
        StrongerSocketActive = null;
        WeakerSocket = null;
        WeakerSocketActive = null;
      }

      public List<BlockLocation> GetBlockLocator()
      {
        return Locator.BlockList;
      }

      public bool isStrongerThan(ChainSocket socket)
      {
        if (socket == null)
        {
          return true;
        }
        return AccumulatedDifficulty > socket.AccumulatedDifficulty;
      }
      public bool isProbeStrongerThan(ChainSocket socket)
      {
        if (socket == null)
        {
          return true;
        }
        return Probe.isStrongerThan(socket.Probe);
      }
      
    }
  }
}
