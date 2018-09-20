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
      public UInt256 HashBlockTip { get; private set; }
      ChainBlock BlockUnassignedPayloadDeepest;
      ChainBlock BlockGenesis;

      double AccumulatedDifficulty;
      public uint HeightBlockTip { get; private set; }

      ChainSocket StrongerSocket;
      public ChainSocket WeakerSocket { get; private set; }

      public SocketProbeHeader HeaderProbe { get; private set; }



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

        if(block.BlockStore == null)
        {
          BlockUnassignedPayloadDeepest = block;
        }
        
        AccumulatedDifficulty = accumulatedDifficultyPrevious + TargetManager.GetDifficulty(block.Header.NBits);
        HeightBlockTip = height;

        HeaderProbe = new SocketProbeHeader(this);
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

      public SocketProbeHeader GetProbeAtBlock(UInt256 hash)
      {
        HeaderProbe.Reset();

        while (true)
        {
          if (HeaderProbe.IsHash(hash))
          {
            return HeaderProbe;
          }

          if (HeaderProbe.IsGenesis())
          {
            return null;
          }

          HeaderProbe.Push();
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
          return HashBlockTip;
        }

        return block.BlocksNext[0].Header.HashPrevious;
      }
    }
  }
}
