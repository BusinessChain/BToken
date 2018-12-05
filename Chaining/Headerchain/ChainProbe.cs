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

      List<ChainHeader> TrailTowardTip;


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

        TrailTowardTip = new List<ChainHeader>();
      }

      public bool GoTo(UInt256 hash)
      {
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
        SetTrailTowardTip();

        Hash = Header.NetworkHeader.HashPrevious;
        Header = Header.HeaderPrevious;
        AccumulatedDifficulty -= TargetManager.GetDifficulty(Header.NetworkHeader.NBits);
        
        Depth++;
      }
      void SetTrailTowardTip()
      {
        if(Header.HeaderPrevious.HeadersNext.First() != Header)
        TrailTowardTip.Insert(0, Header);
      }

      public void Pull()
      {
        Header = GetNextHeaderTowardTip();
        Hash = GetHeaderHash(Header);
        AccumulatedDifficulty += TargetManager.GetDifficulty(Header.NetworkHeader.NBits);

        Depth--;
      }
      ChainHeader GetNextHeaderTowardTip()
      {
        bool useNextTrail = Header.HeadersNext.Count > 1 && TrailTowardTip.Any() && Header.HeadersNext.Contains(TrailTowardTip.First());
        if (useNextTrail)
        {
          ChainHeader nextHeaderTrail = TrailTowardTip.First();
          TrailTowardTip.Remove(nextHeaderTrail);
          return nextHeaderTrail;
        }
        else
        {
          return Header.HeadersNext.First();
        }
      }

      public UInt256 GetHeaderHash(ChainHeader header)
      {
        if (header.HeadersNext.Any())
        {
          return header.HeadersNext[0].NetworkHeader.HashPrevious;
        }
        else if (header == Chain.HeaderTip)
        {
          return Chain.HeaderTipHash;
        }
        else
        {
          return header.NetworkHeader.GetHeaderHash();
        }
      }

      public bool IsTip() => Header == Chain.HeaderTip;
      public uint GetHeight() => Chain.Height - Depth;

    }
  }
}
