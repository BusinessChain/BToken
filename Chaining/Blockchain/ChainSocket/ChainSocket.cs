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

      ChainBlock BlockTip;
      ChainBlock BlockNoPayloadAssignedDeepest;
      ChainBlock BlockGenesis;
      public UInt256 HashBlockTip { get; private set; }

      double AccumulatedDifficulty;
      public uint HeightBlockTip { get; private set; }

      public ChainSocket StrongerSocket { get; private set; }
      public ChainSocket WeakerSocket { get; private set; }

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
        BlockTip = block;
        HashBlockTip = hash;

        AccumulatedDifficulty = accumulatedDifficultyPrevious + TargetManager.GetDifficulty(block.Header.NBits);
        HeightBlockTip = height;

        Probe = new SocketProbe(this);
      }

      public SocketProbe GetProbeAtBlock_NormalSearch(UInt256 hash)
      {
        Probe.Reset();

        while (true)
        {
          if (Probe.IsHash(hash))
          {
            return Probe;
          }

          if (Probe.IsGenesis())
          {
            return null;
          }

          Probe.Push();
        }
      }

      public SocketProbe GetProbeAtBlock_UnassignedPayloadSearch(UInt256 hash)
      {
        Probe.GoToBlockNoPayloadAssignedDeepest();

        while (true)
        {
          if (Probe.IsHash(hash))
          {
            return Probe;
          }

          if (Probe.IsTip())
          {
            return null;
          }

          Probe.Pull();
        }
      }

      public void ConnectWeakerSocket(ChainSocket weakerSocket)
      {
        weakerSocket.WeakerSocket = WeakerSocket;
        weakerSocket.StrongerSocket = this;

        if (WeakerSocket != null)
        {
          WeakerSocket.StrongerSocket = weakerSocket;
        }

        WeakerSocket = weakerSocket;
      }
      
      void ConnectNextBlock(ChainBlock block, UInt256 headerHash)
      {
        BlockTip = block;
        HashBlockTip = headerHash;
        AccumulatedDifficulty += TargetManager.GetDifficulty(block.Header.NBits);
        HeightBlockTip++;
      }

      void Disconnect()
      {
        if (StrongerSocket != null)
        {
          StrongerSocket.WeakerSocket = WeakerSocket;
        }
        if (WeakerSocket != null)
        {
          WeakerSocket.StrongerSocket = StrongerSocket;
        }

        StrongerSocket = null;
        WeakerSocket = null;
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
