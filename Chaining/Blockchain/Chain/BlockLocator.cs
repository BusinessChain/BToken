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
        void SortLocator() => SortLocatorRecursive(1);
        void SortLocatorRecursive(int startIndex)
        {
          if (startIndex >= BlockLocations.Count - 2)
          {
            return;
          }

          uint depthFromPrior = BlockLocations[startIndex - 1].Height - BlockLocations[startIndex].Height;
          uint heightFromNext = BlockLocations[startIndex].Height - BlockLocations[startIndex + 2].Height;

          if (heightFromNext <= 2 * depthFromPrior)
          {
            BlockLocations.RemoveAt(startIndex + 1);
            SortLocatorRecursive(startIndex + 1);
          }

        }
      }
    }
  }
}
