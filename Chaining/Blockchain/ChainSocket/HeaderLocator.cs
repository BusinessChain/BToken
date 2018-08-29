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
      class HeaderLocator
      {
        ChainSocket Socket;

        public List<BlockLocation> HeaderList { get; private set; }


        public HeaderLocator(ChainSocket socket)
        {
          Socket = socket;

          HeaderList = CreateHeaderList();
        }
        List<BlockLocation> CreateHeaderList()
        {
          List<BlockLocation> headerLocator = new List<BlockLocation>();
          Socket.Probe.reset();
          uint locator = 0;

          while (!Socket.Probe.IsGenesis() && !Socket.Blockchain.Checkpoints.IsCheckpoint(Socket.Probe.GetHeight()))
          {
            if (locator == Socket.Probe.Depth)
            {
              headerLocator.Add(Socket.Probe.GetBlockLocation());
              locator = GetNextLocator(locator);
            }

            Socket.Probe.push();
          }

          headerLocator.Add(Socket.Probe.GetBlockLocation());

          return headerLocator;
        }
        uint GetNextLocator(uint locator)
        {
          return locator * 2 + 1;
        }

        public void Update(uint height, UInt256 hash)
        {
          var newBlockLocation = new BlockLocation(height, hash);

          if(Socket.Blockchain.Checkpoints.IsCheckpoint(height))
          {
            HeaderList = new List<BlockLocation>() { newBlockLocation };
          }
          else
          {
            HeaderList.Insert(0, newBlockLocation);
            SortLocator();
          }
        }
        void SortLocator()
        {
          SortLocator(1);
        }
        void SortLocator(int n)
        {
          if (n >= HeaderList.Count - 2)
          {
            return;
          }

          uint depthFromPrior = HeaderList[n - 1].Height - HeaderList[n].Height;
          uint heightFromNextNext = HeaderList[n].Height - HeaderList[n + 2].Height;

          if (heightFromNextNext <= 2 * depthFromPrior)
          {
            HeaderList.RemoveAt(n + 1);
            SortLocator(n + 1);
          }

        }
        
        
      }
    }
  }
}
