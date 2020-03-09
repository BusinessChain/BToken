using System;
using System.Collections.Generic;
using System.Linq;

namespace BToken.Chaining
{
  partial class Headerchain
  {
    public class Chain
    {
      public Header HeaderRoot;
      public Header HeaderTip;
      public int Height;
      public double AccumulatedDifficulty;


      public Chain(
        Header headerRoot,
        int height,
        double accumulatedDifficulty)
      {
        HeaderRoot = headerRoot;
        HeaderTip = headerRoot;
        Height = height;
        AccumulatedDifficulty = accumulatedDifficulty;
      }

      public void ExtendChain(Header header)
      {
        HeaderTip = header;
        Height++;
        AccumulatedDifficulty += TargetManager.GetDifficulty(header.NBits);
      }

      public bool IsStrongerThan(Chain chain)
      {
        if(chain == null)
        {
          return true;
        }

        return AccumulatedDifficulty > chain.AccumulatedDifficulty;
      }
    }
  }
}
