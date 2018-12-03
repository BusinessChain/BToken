using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using BToken.Networking;

namespace BToken.Chaining
{
  partial class Headerchain
  {
    class ChainProbe
    {
      public Chain Chain;

      public ChainHeader Header;
      public UInt256 Hash;
      public double AccumulatedDifficulty;
      public uint Depth;


      public ChainProbe(Chain chain)
      {
        Chain = chain;

        Initialize();
      }

      public void Initialize()
      {
        Header = Chain.HeaderTip;
        Hash = Chain.HeaderTipHash;
        AccumulatedDifficulty = Chain.AccumulatedDifficulty;
        Depth = 0;
      }

      public bool GoTo(UInt256 hash)
      {
        Initialize();

        while (true)
        {
          if (Hash.IsEqual(hash))
          {
            return true;
          }

          if (Header == Chain.HeaderRoot)
          {
            return false;
          }

          Push();
        }
      }
      public void Push()
      {
        Hash = Header.NetworkHeader.HashPrevious;
        Header = Header.HeaderPrevious;
        AccumulatedDifficulty -= TargetManager.GetDifficulty(Header.NetworkHeader.NBits);

        Depth++;
      }
      
      public bool IsTip() => Header == Chain.HeaderTip;
      public uint GetHeight() => Chain.Height - Depth;

    }
  }
}
