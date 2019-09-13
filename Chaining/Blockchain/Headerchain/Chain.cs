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
        public Header HeaderTip;
        public int Height;
        public double AccumulatedDifficulty;
        public Header HeaderRoot;


        public Chain(
          Header headerRoot,
          int height,
          double accumulatedDifficulty)
        {
          HeaderTip = headerRoot;
          Height = height;
          HeaderRoot = headerRoot;
          AccumulatedDifficulty = accumulatedDifficulty;
        }

        public void ExtendChain(Header header)
        {
          HeaderTip = header;
          Height++;
          AccumulatedDifficulty += TargetManager.GetDifficulty(header.NBits);
        }

        public bool IsStrongerThan(Chain chain) => chain == null ? true : AccumulatedDifficulty > chain.AccumulatedDifficulty;
      }
    }
  }
}
