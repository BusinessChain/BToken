using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace BToken.Chaining
{
  public partial class Blockchain
  {
    class HeaderLocator
    {
      Blockchain Blockchain;

      public List<BlockLocation> BlockLocations { get; private set; }


      public HeaderLocator(Blockchain blockchain, ChainSocket.SocketProbeHeader socketProbe)
      {
        Blockchain = blockchain;

        BlockLocations = Create(socketProbe);
      }


      public List<BlockLocation> Create(ChainSocket.SocketProbeHeader socketProbe)
      {
        List<BlockLocation> headerLocator = new List<BlockLocation>();
        socketProbe.Reset();
        uint locator = 0;

        while (socketProbe.GetHeight() > 0 && !Blockchain.Checkpoints.IsCheckpoint(socketProbe.GetHeight()))
        {
          if (locator == socketProbe.Depth)
          {
            headerLocator.Add(socketProbe.GetBlockLocation());
            locator = GetNextLocator(locator);
          }

          socketProbe.Push();
        }

        headerLocator.Add(socketProbe.GetBlockLocation());

        return headerLocator;
      }
      uint GetNextLocator(uint locator) => locator * 2 + 1;

      public void Update(uint height, UInt256 hash)
      {
        var newBlockLocation = new BlockLocation(height, hash);

        if (Blockchain.Checkpoints.IsCheckpoint(height))
        {
          BlockLocations = new List<BlockLocation>() { newBlockLocation };
        }
        else
        {
          BlockLocations.Insert(0, newBlockLocation);
          SortLocator();
        }
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
