using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace BToken.Chaining
{
  public partial class Blockchain
  {
    partial class Chain
    {
      class BlockLocator
      {
        public List<BlockLocation> BlockLocations { get; private set; } = new List<BlockLocation>();


        public BlockLocator(uint height, UInt256 hash)
        {
          Update(height, hash);
        }

        public void Update(uint height, UInt256 hash)
        {
          var newBlockLocation = new BlockLocation(height, hash);
          BlockLocations.Insert(0, newBlockLocation);

          SortLocator();
        }
        void SortLocator() => SortLocator(1);
        void SortLocator(int n)
        {
          if (n >= BlockLocations.Count - 2)
          {
            return;
          }

          uint depthFromPrior = BlockLocations[n - 1].Height - BlockLocations[n].Height;
          uint heightFromNextNext = BlockLocations[n].Height - BlockLocations[n + 2].Height;

          if (heightFromNextNext <= 2 * depthFromPrior)
          {
            BlockLocations.RemoveAt(n + 1);
            SortLocator(n + 1);
          }

        }
      }
    }
  }
}
