using System;
using System.Collections.Generic;
using System.Linq;

using BToken.Networking;


namespace BToken.Chaining
{
  partial class Blockchain
  {
    partial class Chain
    {
      public ChainBlock BlockTip;
      public UInt256 BlockTipHash;
      public uint BlockTipHeight;
      public double AccumulatedDifficulty;

      public ChainBlock BlockGenesis;
      public ChainBlock BlockHighestAssigned;

      BlockLocator Locator;



      public Chain(ChainBlock genesisBlock)
      {
        UInt256 blockGenesisHash = new UInt256(Hashing.SHA256d(genesisBlock.Header.GetBytes()));

        BlockTip = genesisBlock;
        BlockTipHash = blockGenesisHash;
        BlockTipHeight = 0;
        BlockGenesis = genesisBlock;
        AccumulatedDifficulty = TargetManager.GetDifficulty(genesisBlock.Header.NBits);
        
        Locator = new BlockLocator(0, blockGenesisHash);
      }

      public Chain(
        ChainBlock blockTip,
        UInt256 blockTipHash,
        uint blockTipHeight,
        ChainBlock blockGenesis,
        ChainBlock blockHighestAssigned,
        double accumulatedDifficultyPrevious,
        BlockLocator blockLocator)
      {
        BlockTip = blockTip;
        BlockTipHash = blockTipHash;
        BlockTipHeight = blockTipHeight;
        BlockGenesis = blockGenesis;
        BlockHighestAssigned = blockHighestAssigned;
        AccumulatedDifficulty = accumulatedDifficultyPrevious + TargetManager.GetDifficulty(blockTip.Header.NBits);
        Locator = blockLocator;

      }
            
      public List<BlockLocation> GetBlockLocations() => Locator.BlockLocations;
      void UpdateLocator() => Locator.Update(BlockTipHeight, BlockTipHash);
      
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
        BlockTipHeight++;
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

      public uint GetHeight() => BlockTipHeight;
      public bool IsStrongerThan(Chain chain) => chain == null ? true : AccumulatedDifficulty > chain.AccumulatedDifficulty;
    }
  }
}
