using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace BToken.Chaining
{
  public partial class Blockchain
  {
    class BlockLocator
    {
      Blockchain Blockchain;
      public List<BlockLocation> BlockLocations { get; private set; } = new List<BlockLocation>();


      public BlockLocator(Blockchain blockchain)
      {
        Blockchain = blockchain;

        Update();
      }

      public void Reorganize()
      {
        
        UInt256 hash = Blockchain.MainChain.BlockTipHash;
        uint height = Blockchain.MainChain.Height;

        BlockLocations = new List<BlockLocation>() { new BlockLocation(height, hash) };


        ChainBlock block = Blockchain.MainChain.BlockTip;
        uint depth = 0;
        uint nextLocationDepth = 1;

        do
        {
          if (depth == nextLocationDepth)
          {
            BlockLocations.Add(new BlockLocation(height, hash));
            nextLocationDepth *= 2;
          }

          depth++;
          height--;
          hash = block.Header.HashPrevious;

          block = block.BlockPrevious;
        } while (height > 0);

        BlockLocations.Add(new BlockLocation(height, hash)); // must be Genesis Location

      }

      public void Update()
      {
        uint height = Blockchain.MainChain.Height;
        UInt256 hash = Blockchain.MainChain.BlockTipHash;

        AddLocation(height, hash);
      }

      void AddLocation(uint height, UInt256 hash)
      {
        BlockLocations.Insert(0, new BlockLocation(height, hash));

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
        }

        SortLocatorRecursive(startIndex + 1);

      }
    }
  }
}
