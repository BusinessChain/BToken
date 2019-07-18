using System;
using System.Collections.Generic;
using System.Linq;

namespace BToken.Chaining
{
  public partial class Headerchain
  {
    class Chain
    {
      public ChainHeader HeaderTip { get; private set; }
      public byte[] HeaderTipHash { get; private set; }
      public int Height { get; private set; }
      public double AccumulatedDifficulty { get; private set; }
      public ChainHeader HeaderRoot { get; private set; }


      public Chain(
        ChainHeader headerRoot,
        int height,
        double accumulatedDifficultyPrevious)
      {
        HeaderTip = headerRoot;
        HeaderTipHash = headerRoot.GetHeaderHash();
        Height = height;
        HeaderRoot = headerRoot;
        AccumulatedDifficulty = accumulatedDifficultyPrevious + TargetManager.GetDifficulty(headerRoot.NetworkHeader.NBits);
      }

      public void ExtendChain(ChainHeader header, byte[] headerHash)
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
