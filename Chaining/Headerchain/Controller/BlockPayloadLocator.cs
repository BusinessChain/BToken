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
  partial class Headerchain
  {
    class BlockPayloadLocator
    {
      Headerchain Blockchain;

      int BatchSizeQueue;
      List<ChainHeader> BlocksQueued = new List<ChainHeader>();

      const int BatchSizeDispatch = 50;

      BlockingCollection<ChainHeader> BlocksDispatched;


      public BlockPayloadLocator(Headerchain blockchain)
      {
        Blockchain = blockchain;
      }

      public List<ChainHeader> DispatchBlocks()
      {
        var blocksDispatched = new List<ChainHeader>();

        do
        {
          while (BlocksQueued.Count > 0)
          {
            ChainHeader blockQueued = PopBlockQueued();

            blocksDispatched.Add(blockQueued);
            BlocksDispatched.Add(blockQueued);

            if (blocksDispatched.Count == BatchSizeDispatch)
            {
              return blocksDispatched;
            }
          }

          List<ChainHeader> blocksQueued = new List<ChainHeader>();//Blockchain.GetBlocksUnassignedPayload(BatchSizeQueue);
          IEnumerable<ChainHeader> blocksQueuedNotYetDispatched = blocksQueued.Except(BlocksDispatched);
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

      ChainHeader PopBlockQueued()
      {
        ChainHeader blockQueued = BlocksQueued[0];

        BlocksQueued.RemoveAt(0);

        return blockQueued;
      }
    }
  }
}
