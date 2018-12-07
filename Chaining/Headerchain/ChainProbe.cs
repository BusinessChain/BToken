using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using BToken.Networking;

namespace BToken.Chaining
{
  public partial class Blockchain
  {
    partial class Headerchain
    {
      public class ChainProbe
      {
        public Chain Chain;

        public ChainHeader Header;
        public UInt256 Hash;
        public uint Depth;

        List<ChainHeader> Trail;


        public ChainProbe(Chain chain)
        {
          Chain = chain;

          Initialize();
        }

        public virtual void Initialize()
        {
          Header = Chain.HeaderTip;
          Hash = Chain.HeaderTipHash;
          Depth = 0;

          Trail = new List<ChainHeader>();
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
            if (IsRoot())
            {
              return false;
            }

            Push();
          }
        }
        public virtual void Push()
        {
          LayTrail();

          Hash = Header.NetworkHeader.HashPrevious;
          Header = Header.HeaderPrevious;

          Depth++;
        }
        void LayTrail()
        {
          if (Header.HeaderPrevious.HeadersNext.First() != Header)
            Trail.Insert(0, Header);
        }

        public void Pull()
        {
          Header = GetHeaderTowardTip();
          Hash = GetHeaderHash(Header);

          Depth--;
        }
        ChainHeader GetHeaderTowardTip()
        {
          if (Header.HeadersNext.Count == 0)
          {
            return null;
          }

          bool useTrail = Header.HeadersNext.Count > 1
            && Trail.Any()
            && Header.HeadersNext.Contains(Trail.First());

          if (useTrail)
          {
            ChainHeader headerTrail = Trail.First();
            Trail.Remove(headerTrail);
            return headerTrail;
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
        public bool IsRoot() => Header == Chain.HeaderRoot;
        public uint GetHeight() => Chain.Height - Depth;

      }
    }
  }
}
