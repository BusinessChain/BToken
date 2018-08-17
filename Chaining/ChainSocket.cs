using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace BToken.Chaining
{
  partial class Blockchain
  {
    partial class ChainSocket
    {
      Blockchain Blockchain;

      static ChainBlock BlockGenesis;
      public ChainBlock Block;

      double AccumulatedDifficulty;
      public uint Height;

      public ChainSocket StrongerSocket;
      public ChainSocket StrongerSocketActive;
      public ChainSocket WeakerSocket;
      public ChainSocket WeakerSocketActive;

      public SocketProbe Probe;


      public ChainSocket(
        Blockchain blockchain, 
        ChainBlock blockGenesis,
        double accumulatedDifficultyPrevious,
        uint height
        )
      {
        Blockchain = blockchain;

        BlockGenesis = blockGenesis;
        Block = blockGenesis;

        AccumulatedDifficulty = accumulatedDifficultyPrevious + TargetManager.GetDifficulty(blockGenesis.NBits);
        Height = height;

        Probe = new SocketProbe(this);
      }

      public bool isWeakerSocketProbeStrongerThan(ChainSocket socket)
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
      public ChainSocket InsertBlock(ChainBlock block)
      {
        return Probe.InsertBlock(block);
      }

      public void AppendChainHeader(ChainBlock block)
      {
        Block = block;
        AccumulatedDifficulty += TargetManager.GetDifficulty(block.NBits);
        Height++;
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

      public bool isProbeAtGenesis()
      {
        return BlockGenesis == Probe.Block;
      }

    }
  }
}
