using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace BToken.Chaining
{
  partial class Blockchain
  {
    public class HeaderLocator
    {
      public List<HeaderLocation> Locations = 
        new List<HeaderLocation>();



      public void Generate(int height, Header header)
      {
        Locations.Clear();

        byte[] hash = header.Hash;        
        uint depth = 0;
        uint nextLocationDepth = 1;

        Locations.Add(new HeaderLocation(height, hash));

        do
        {
          if (depth == nextLocationDepth)
          {
            Locations.Add(
              new HeaderLocation(height, hash));

            nextLocationDepth *= 2;
          }

          depth++;
          height--;
          hash = header.HashPrevious;

          header = header.HeaderPrevious;
        } while (height > 0);
        
        Locations.Add(new HeaderLocation(0, hash)); 
      }
      
      public void AddLocation(int height, byte[] hash)
      {
        Locations.Insert(
          0, 
          new HeaderLocation(height, hash));

        SortLocatorRecursive(1);
      }
      void SortLocatorRecursive(int startIndex)
      {
        if (startIndex >= Locations.Count - 2)
        {
          return;
        }

        int depthFromPrior = Locations[startIndex - 1].Height - 
          Locations[startIndex].Height;

        int heightFromNext = Locations[startIndex].Height - 
          Locations[startIndex + 2].Height;

        if (heightFromNext <= 2 * depthFromPrior)
        {
          Locations.RemoveAt(startIndex + 1);
        }

        SortLocatorRecursive(startIndex + 1);
      }
    }
  }
}
