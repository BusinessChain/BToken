using System;
using System.Collections.Generic;
using System.Linq;

namespace BToken.Chaining
{
  public partial class Blockchain
  {
    public partial class Headerchain
    {
      class Chain
      {
        public Header HeaderTip { get; private set; }
        public byte[] HeaderTipHash { get; private set; }
        public int Height { get; private set; }
        public double AccumulatedDifficulty { get; private set; }
        public Header HeaderRoot { get; private set; }


        public Chain(
          Header headerRoot,
          int height,
          double accumulatedDifficultyPrevious)
        {
          HeaderTip = headerRoot;
          HeaderTipHash = headerRoot.HeaderHash;
          Height = height;
          HeaderRoot = headerRoot;
          AccumulatedDifficulty = accumulatedDifficultyPrevious + TargetManager.GetDifficulty(headerRoot.NBits);
        }

        public void ExtendChain(Header header)
        {
          HeaderTip = header;
          HeaderTipHash = header.HeaderHash;
          Height++;
          AccumulatedDifficulty += TargetManager.GetDifficulty(header.NBits);
        }

        public bool IsStrongerThan(Chain chain) => chain == null ? true : AccumulatedDifficulty > chain.AccumulatedDifficulty;
      }
    }
  }
}
