using System.Diagnostics;

using System;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Linq;
using System.Threading;
using System.Text;
using System.Threading.Tasks;

namespace BToken.Chaining
{
  public partial class Blockchain
  {
    class BlockPayloadLocator
    {
      Blockchain Blockchain;

      int BatchSizeQueue;
      List<ChainBlock> BlocksQueued = new List<ChainBlock>();

      const int BatchSizeDispatch = 50;

      BlockingCollection<ChainBlock> BlocksDispatched;


      public BlockPayloadLocator(Blockchain blockchain)
      {
        Blockchain = blockchain;
      }

      public List<ChainBlock> DispatchBlocks()
      {
        var blocksDispatched = new List<ChainBlock>();

        do
        {
          while (BlocksQueued.Count > 0)
          {
            ChainBlock blockQueued = PopBlockQueued();

            blocksDispatched.Add(blockQueued);
            BlocksDispatched.Add(blockQueued);

            if (blocksDispatched.Count == BatchSizeDispatch)
            {
              return blocksDispatched;
            }
          }

          List<ChainBlock> blocksQueued = Blockchain.GetBlocksUnassignedPayload(BatchSizeQueue);
          IEnumerable<ChainBlock> blocksQueuedNotYetDispatched = blocksQueued.Except(BlocksDispatched);
          BlocksQueued = blocksQueuedNotYetDispatched.ToList();

        } while (BlocksQueued.Count > 0);

        if (blocksDispatched.Count > 0)
        {
          return blocksDispatched;
        }
        else
        {
          return BlocksDispatched.Take(BatchSizeDispatch).ToList();
        }

      }

      ChainBlock PopBlockQueued()
      {
        ChainBlock blockQueued = BlocksQueued[0];

        BlocksQueued.RemoveAt(0);

        return blockQueued;
      }
    }
  }
}
