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
      public class HeaderLocator
      {
        Headerchain Headerchain;
        List<HeaderLocation> BlockLocations = new List<HeaderLocation>();


        public HeaderLocator(Headerchain headerchain)
        {
          Headerchain = headerchain;
          Update();
        }

        public void Reorganize()
        {
          byte[] hash = Headerchain.MainChain.HeaderTip.HeaderHash;
          int height = Headerchain.MainChain.Height;

          BlockLocations = new List<HeaderLocation>() { new HeaderLocation(height, hash) };


          Header header = Headerchain.MainChain.HeaderTip;
          uint depth = 0;
          uint nextLocationDepth = 1;

          do
          {
            if (depth == nextLocationDepth)
            {
              BlockLocations.Add(new HeaderLocation(height, hash));
              nextLocationDepth *= 2;
            }

            depth++;
            height--;
            hash = header.HashPrevious;

            header = header.HeaderPrevious;
          } while (height > 0);

          BlockLocations.Add(new HeaderLocation(height, hash)); // must be Genesis Location

        }

        public void Update()
        {
          int height = Headerchain.MainChain.Height;
          byte[] hash = Headerchain.MainChain.HeaderTip.HeaderHash;

          AddLocation(height, hash);
        }
        public IEnumerable<byte[]> GetHeaderHashes()
        {
          return BlockLocations.Select(b => b.Hash);
        }

        void AddLocation(int height, byte[] hash)
        {
          BlockLocations.Insert(0, new HeaderLocation(height, hash));

          SortLocatorRecursive(1);
        }
        void SortLocatorRecursive(int startIndex)
        {
          if (startIndex >= BlockLocations.Count - 2)
          {
            return;
          }

          int depthFromPrior = BlockLocations[startIndex - 1].Height - BlockLocations[startIndex].Height;
          int heightFromNext = BlockLocations[startIndex].Height - BlockLocations[startIndex + 2].Height;

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
