using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace BToken.Chaining
{
  public partial class Blockchain
  {
    partial class ChainSocket
    {
      Blockchain Blockchain;

      ChainBlock BlockGenesis;
      ChainBlock Block;
      public UInt256 Hash { get; private set; }

      double AccumulatedDifficulty;
      public uint Height { get; private set; }

      public ChainSocket StrongerSocket { get; private set; }
      public ChainSocket StrongerSocketActive { get; private set; }
      public ChainSocket WeakerSocket { get; private set; }
      public ChainSocket WeakerSocketActive { get; private set; }

      public SocketProbe Probe { get; private set; }



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

        BlockGenesis = block;
        Block = block;
        Hash = hash;

        AccumulatedDifficulty = accumulatedDifficultyPrevious + TargetManager.GetDifficulty(block.Header.NBits);
        Height = height;

        Probe = new SocketProbe(this);
      }

      public bool IsWeakerSocketProbeStrongerThan(ChainSocket socket)
      {
        return WeakerSocketActive != null && WeakerSocketActive.IsProbeStrongerThan(socket);
      }
      public bool IsWeakerSocketProbeStronger()
      {
        return WeakerSocketActive != null && WeakerSocketActive.IsProbeStrongerThan(this);
      }
      public void Bypass()
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
      public void Reset()
      {
        Probe.Reset();

        StrongerSocketActive = StrongerSocket;
        WeakerSocketActive = WeakerSocket;
      }

      public void ConnectWeakerSocket(ChainSocket weakerSocket)
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
      
      public bool IsStrongerThan(ChainSocket socket)
      {
        if (socket == null)
        {
          return true;
        }
        return AccumulatedDifficulty > socket.AccumulatedDifficulty;
      }
      public bool IsProbeStrongerThan(ChainSocket socket)
      {
        if (socket == null)
        {
          return true;
        }
        return Probe.IsStrongerThan(socket.Probe);
      }
            
    }
  }
}
