using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace BToken.Chaining
{
  public partial class Headerchain
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
        UInt256 hash = Headerchain.MainChain.HeaderTipHash;
        int height = Headerchain.MainChain.Height;

        BlockLocations = new List<HeaderLocation>() { new HeaderLocation(height, hash) };


        ChainHeader block = Headerchain.MainChain.HeaderTip;
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
          hash = block.NetworkHeader.HashPrevious;

          block = block.HeaderPrevious;
        } while (height > 0);

        BlockLocations.Add(new HeaderLocation(height, hash)); // must be Genesis Location

      }

      public void Update()
      {
        int height = Headerchain.MainChain.Height;
        UInt256 hash = Headerchain.MainChain.HeaderTipHash;

        AddLocation(height, hash);
      }
      public List<UInt256> ToList()
      {
        return BlockLocations.Select(b => b.Hash).ToList();
      }

      void AddLocation(int height, UInt256 hash)
      {
        BlockLocations.Insert(0, new HeaderLocation(height, hash));

        SortLocator();
      }
      void SortLocator() => SortLocatorRecursive(1);
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
