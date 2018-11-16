using System;
using System.Collections.Generic;
using System.Linq;


namespace BToken.Chaining
{
  partial class Headerchain
  {
    class Chain
    {
      public ChainHeader HeaderTip { get; private set; }
      public UInt256 HeaderTipHash { get; private set; }
      public uint Height { get; private set; }
      public double AccumulatedDifficulty { get; private set; }

      public ChainHeader HeaderRoot { get; private set; }



      public Chain(ChainHeader headerRoot)
      {
        HeaderTip = headerRoot;
        HeaderTipHash = new UInt256(Hashing.SHA256d(headerRoot.NetworkHeader.GetBytes()));
        Height = 0;
        HeaderRoot = headerRoot;
        AccumulatedDifficulty = TargetManager.GetDifficulty(headerRoot.NetworkHeader.NBits);
      }

      public Chain(
        ChainHeader headerTip,
        UInt256 headerTipHash,
        uint headerTipHeight,
        ChainHeader headerRoot,
        double accumulatedDifficultyPrevious)
      {
        HeaderTip = headerTip;
        HeaderTipHash = headerTipHash;
        Height = headerTipHeight;
        HeaderRoot = headerRoot;
        AccumulatedDifficulty = accumulatedDifficultyPrevious + TargetManager.GetDifficulty(headerTip.NetworkHeader.NBits);
      }            
      
      public UInt256 GetHeaderHash(ChainHeader header)
      {
        if (header == HeaderTip)
        {
          return HeaderTipHash;
        }

        return header.HeadersNext[0].NetworkHeader.HashPrevious;
      }

      public void ExtendChain(ChainHeader header, UInt256 headerHash)
      {
        HeaderTip = header;
        HeaderTipHash = headerHash;
        Height++;
        AccumulatedDifficulty += TargetManager.GetDifficulty(header.NetworkHeader.NBits);
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
