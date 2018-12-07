using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace BToken.Chaining
{
  public partial class Blockchain
  {
    partial class Headerchain
    {
      class HeaderLocator
      {
        Headerchain Headerchain;
        List<ChainLocation> BlockLocations = new List<ChainLocation>();


        public HeaderLocator(Headerchain headerchain)
        {
          Headerchain = headerchain;

          Update();
        }

        public void Reorganize()
        {
          UInt256 hash = Headerchain.MainChain.HeaderTipHash;
          uint height = Headerchain.MainChain.Height;

          BlockLocations = new List<ChainLocation>() { new ChainLocation(height, hash) };


          ChainHeader block = Headerchain.MainChain.HeaderTip;
          uint depth = 0;
          uint nextLocationDepth = 1;

          do
          {
            if (depth == nextLocationDepth)
            {
              BlockLocations.Add(new ChainLocation(height, hash));
              nextLocationDepth *= 2;
            }

            depth++;
            height--;
            hash = block.NetworkHeader.HashPrevious;

            block = block.HeaderPrevious;
          } while (height > 0);

          BlockLocations.Add(new ChainLocation(height, hash)); // must be Genesis Location

        }

        public void Update()
        {
          uint height = Headerchain.MainChain.Height;
          UInt256 hash = Headerchain.MainChain.HeaderTipHash;

          AddLocation(height, hash);
        }
        public List<UInt256> GetHeaderLocator()
        {
          return BlockLocations.Select(b => b.Hash).ToList();
        }

        void AddLocation(uint height, UInt256 hash)
        {
          BlockLocations.Insert(0, new ChainLocation(height, hash));

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
}
