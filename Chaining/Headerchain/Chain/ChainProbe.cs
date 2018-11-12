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

      void Initialize()
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
      void Push()
      {
        Hash = Header.Header.HashPrevious;
        Header = Header.HeaderPrevious;
        AccumulatedDifficulty -= TargetManager.GetDifficulty(Header.Header.NBits);

        Depth++;
      }

      public void ConnectHeader(NetworkHeader header)
      {
        var chainHeader = new ChainHeader(header);

        chainHeader.HeaderPrevious = Header;
        Header.HeadersNext.Add(chainHeader);
      }
      public void ExtendChain(UInt256 headerHash)
      {
        ChainHeader block = Header.HeadersNext.Last();
        Chain.ExtendChain(block, headerHash);
      }
      public void ForkChain(UInt256 headerHash)
      {
        ChainHeader header = Header.HeadersNext.Last();
        uint headerTipHeight = GetHeight() + 1;

        Chain = new Chain(
          headerTip: header,
          headerTipHash: headerHash,
          headerTipHeight: headerTipHeight,
          headerRoot: header,
          accumulatedDifficultyPrevious: AccumulatedDifficulty);

      }
      
      public bool IsTip() => Header == Chain.HeaderTip;
      public uint GetHeight() => Chain.Height - Depth;

    }
  }
}
