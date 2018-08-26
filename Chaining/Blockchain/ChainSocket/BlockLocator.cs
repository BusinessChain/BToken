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
        ChainSocket Socket;

        public List<BlockLocation> BlockList { get; private set; }


        public BlockLocator(ChainSocket socket)
        {
          Socket = socket;

          BlockList = CreateBlockList();
        }
        List<BlockLocation> CreateBlockList()
        {
          List<BlockLocation> blockLocator = new List<BlockLocation>();
          Socket.Probe.reset();
          uint locator = 0;

          while (!Socket.Probe.IsGenesis())
          {
            if (locator == Socket.Probe.Depth)
            {
              blockLocator.Add(Socket.Probe.GetBlockLocation());
              locator = GetNextLocator(locator);
            }

            Socket.Probe.push();
          }

          blockLocator.Add(Socket.Probe.GetBlockLocation());

          return blockLocator;
        }
        uint GetNextLocator(uint locator)
        {
          return locator * 2 + 1;
        }

        public void Update(uint height, UInt256 hash)
        {
          BlockList.Insert(0, new BlockLocation(height, hash));
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
