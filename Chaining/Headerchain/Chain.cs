using System;
using System.Collections.Generic;
using System.Linq;

using BToken.Networking;

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
        HeaderTipHash = headerRoot.NetworkHeader.GetHeaderHash();
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

      public void ExtendChain(ChainHeader header, UInt256 headerHash)
      {
        HeaderTip = header;
        HeaderTipHash = headerHash;
        Height++;
        AccumulatedDifficulty += TargetManager.GetDifficulty(header.NetworkHeader.NBits);
      }

      public bool IsStrongerThan(Chain chain) => chain == null ? true : AccumulatedDifficulty > chain.AccumulatedDifficulty;
    }
  }
}
