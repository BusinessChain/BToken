using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace BToken.Chaining
{
  partial class BlockchainController
  {
    class BlockPayloadLocator
    {
      Blockchain Blockchain;

      int BatchSizeQueue;
      List<ChainBlock> BlocksQueued = new List<ChainBlock>();

      const int BatchSizeDispatch = 5;
      List<ChainBlock> BlocksDispatched = new List<ChainBlock>();

      public BlockPayloadLocator(Blockchain blockchain, int consumersCount)
      {
        Blockchain = blockchain;
        BatchSizeQueue = BatchSizeDispatch * consumersCount * 2;
      }

      public List<ChainBlock> DispatchBlocks()
      {
        var blocksDispatched = new List<ChainBlock>();

        do
        {
          while (BlocksQueued.Any())
          {
            ChainBlock blockQueued = PopBlockQueued();

            if (BlocksDispatched.Contains(blockQueued)) { continue; }

            blocksDispatched.Add(blockQueued);

            if (blocksDispatched.Count == BatchSizeDispatch)
            {
              BlocksDispatched.AddRange(blocksDispatched);
              return blocksDispatched;
            }
          }

          BlocksQueued = Blockchain.GetBlocksUnassignedPayload(BatchSizeQueue);

        } while (BlocksQueued.Any());

        return blocksDispatched;
      }

      ChainBlock PopBlockQueued()
      {
        ChainBlock blockQueued = BlocksQueued[0];
        BlocksQueued.RemoveAt(0);

        return blockQueued;
      }


      public void RemoveDispatched(List<ChainBlock> blocks)
      {
        BlocksDispatched = BlocksDispatched.Except(blocks).ToList();
      }
    }
  }
}
