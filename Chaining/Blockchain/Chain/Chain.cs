using System;
using System.Collections.Generic;
using System.Linq;

using BToken.Networking;


namespace BToken.Chaining
{
  partial class Blockchain
  {
    class Chain
    {
      public ChainBlock BlockTip { get; private set; }
      public UInt256 BlockTipHash { get; private set; }
      public uint Height { get; private set; }
      public double AccumulatedDifficulty { get; private set; }

      public ChainBlock BlockRoot { get; private set; }
      public ChainBlock BlockHighestAssigned { get; private set; }



      public Chain(ChainBlock blockRoot)
      {
        BlockTip = blockRoot;
        BlockTipHash = new UInt256(Hashing.SHA256d(blockRoot.Header.GetBytes()));
        Height = 0;
        BlockRoot = blockRoot;
        AccumulatedDifficulty = TargetManager.GetDifficulty(blockRoot.Header.NBits);
      }

      public Chain(
        ChainBlock blockTip,
        UInt256 blockTipHash,
        uint blockTipHeight,
        ChainBlock blockRoot,
        ChainBlock blockHighestAssigned,
        double accumulatedDifficultyPrevious)
      {
        BlockTip = blockTip;
        BlockTipHash = blockTipHash;
        Height = blockTipHeight;
        BlockRoot = blockRoot;
        BlockHighestAssigned = blockHighestAssigned;
        AccumulatedDifficulty = accumulatedDifficultyPrevious + TargetManager.GetDifficulty(blockTip.Header.NBits);
      }            
      
      public UInt256 GetHeaderHash(ChainBlock block)
      {
        if (block == BlockTip)
        {
          return BlockTipHash;
        }

        return block.BlocksNext[0].Header.HashPrevious;
      }

      public void ExtendChain(ChainBlock block, UInt256 headerHash)
      {
        BlockTip = block;
        BlockTipHash = headerHash;
        Height++;
        AccumulatedDifficulty += TargetManager.GetDifficulty(block.Header.NBits);
      }

      //public List<ChainBlock> GetBlocksUnassignedPayload(int batchSize)
      //{
      //  if (Socket.BlockHighestAssigned == Socket.BlockTip) { return new List<ChainBlock>(); }

      //  Probe.Block = Socket.BlockHighestAssigned.BlocksNext[0];

      //  var blocksUnassignedPayload = new List<ChainBlock>();
      //  while (blocksUnassignedPayload.Count < batchSize)
      //  {
      //    Socket.BlockHighestAssigned = Probe.Block;

      //    if (Probe.Block.BlockStore == null)
      //    {
      //      blocksUnassignedPayload.Add(Probe.Block);
      //    }

      //    if (Probe.IsTip())
      //    {
      //      return blocksUnassignedPayload;
      //    }

      //    Probe.Block = Probe.Block.BlocksNext[0];
      //  }

      //  return blocksUnassignedPayload;
      //}

      public bool IsStrongerThan(Chain chain) => chain == null ? true : AccumulatedDifficulty > chain.AccumulatedDifficulty;
    }
  }
}
