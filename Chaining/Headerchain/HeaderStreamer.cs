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
      public class HeaderStreamer : ChainProbe
      {
        List<ChainHeader> Trail;


        public HeaderStreamer(Chain chain)
          :base(chain)
        { }

        protected override void Initialize()
        {
          base.Initialize();

          Trail = new List<ChainHeader>();
        }

        public void FindRootLocation(List<UInt256> headerLocator)
        {
          while (true)
          {
            bool isHashInLocator = headerLocator.Any(h => h.IsEqual(Hash));
            if (isHashInLocator || Header == GenesisHeader)
            {
              return;
            }

            Push();
          }
        }

        protected override void Push()
        {
          LayTrail();
          base.Push();
        }
        void LayTrail()
        {
          if (Header.HeaderPrevious.HeadersNext.First() != Header)
            Trail.Insert(0, Header);
        }

        void Pull()
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


        public ChainLocation ReadNextHeaderLocationTowardRoot()
        {
          if (Header != GenesisHeader)
          {
            var chainLocation = new ChainLocation(GetHeight(), Hash);
            Push();

            return chainLocation;
          }

          return null;
        }

        public ChainLocation ReadNextHeaderLocationTowardTip()
        {
          if (GetHeight() > 0)
          {
            var chainLocation = new ChainLocation(GetHeight(), Hash);
            Push();

            return chainLocation;
          }

          return null;
        }

        public NetworkHeader ReadNextHeader()
        {
          if (GetHeight() > 0)
          {
            var header = Header;
            Push();

            return header.NetworkHeader;
          }

          return null;
        }
      }
    }
  }
}
