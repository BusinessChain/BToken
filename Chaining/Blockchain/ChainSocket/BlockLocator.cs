using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace BToken.Chaining
{
  partial class Blockchain
  {
    partial class ChainSocket
    {
      class BlockLocator
      {
        public List<BlockLocation> BlockList { get; private set; } = new List<BlockLocation>();

        public BlockLocator(ChainBlock blockGenesis)
        {
          BlockList.Add(new BlockLocation()
          {
            Height = 0,
            Hash = CalculateHash(blockGenesis.Header.getBytes())
          });
        }

        public void Update(uint height, UInt256 hash)
        {
          BlockList.Insert(0, new BlockLocation()
          {
            Height = height,
            Hash = hash
          });

          SortLocator();
        }

        void SortLocator()
        {
          SortLocator(1);
        }
        void SortLocator(int n)
        {
          if (n >= BlockList.Count - 2)
          {
            return;
          }

          uint depthFromPrior = BlockList[n - 1].Height - BlockList[n].Height;
          uint heightFromNextNext = BlockList[n].Height - BlockList[n + 2].Height;

          if (heightFromNextNext <= 2 * depthFromPrior)
          {
            BlockList.RemoveAt(n + 1);
            SortLocator(n + 1);
          }

        }
        
      }
    }
  }
}
