using System.Diagnostics;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;

using BToken.Networking;

namespace BToken.Chaining
{
  public partial class Blockchain
  {
    partial class ChainSocket
    {
      Blockchain Blockchain;

      ChainBlock BlockTip;
      public UInt256 BlockTipHash { get; private set; }
      public uint BlockTipHeight { get; private set; }

      double AccumulatedDifficulty;

      public SocketProbe Probe { get; private set; }

      ChainBlock BlockUnassignedPayloadDeepest;
      ChainBlock BlockGenesis;
      
      ChainSocket StrongerSocket;
      public ChainSocket WeakerSocket { get; private set; }


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
        BlockTipHash = hash;

        if(block.BlockStore == null)
        {
          BlockUnassignedPayloadDeepest = block;
        }
        
        AccumulatedDifficulty = accumulatedDifficultyPrevious + TargetManager.GetDifficulty(block.Header.NBits);
        BlockTipHeight = height;

        Probe = new SocketProbe(this);
      }

      public List<ChainBlock> GetBlocksUnassignedPayload(int batchSize)
      {
        if (BlockUnassignedPayloadDeepest == null) { return new List<ChainBlock>(); }

        ChainBlock block = BlockUnassignedPayloadDeepest;

        var locatorBatchBlocksUnassignedPayload = new List<ChainBlock>();
        while (locatorBatchBlocksUnassignedPayload.Count < batchSize)
        {
          if (block.BlockStore == null)
          {
            locatorBatchBlocksUnassignedPayload.Add(block);
          }

          if (block == BlockTip)
          {
            return locatorBatchBlocksUnassignedPayload;
          }

          block = block.BlocksNext[0];

          if(locatorBatchBlocksUnassignedPayload.Count == 0)
          {
            BlockUnassignedPayloadDeepest = block;
          }
        }

        return locatorBatchBlocksUnassignedPayload;
      }

      public SocketProbe GetProbeAtBlock(UInt256 hash)
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
        BlockTipHash = headerHash;
        AccumulatedDifficulty += TargetManager.GetDifficulty(block.Header.NBits);
        BlockTipHeight++;

        if(BlockUnassignedPayloadDeepest == null && block.BlockStore == null)
        {
          BlockUnassignedPayloadDeepest = block;
        }
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
      
      UInt256 GetHeaderHash(ChainBlock block)
      {
        if(block == BlockTip)
        {
          return BlockTipHash;
        }

        return block.BlocksNext[0].Header.HashPrevious;
      }
    }
  }
}
