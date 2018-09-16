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

      List<UInt256> BlockLocationsQueued = new List<UInt256>();
      const int BatchSize = 50;

      List<UInt256> BlockLocationsDispatched = new List<UInt256>();

      public BlockPayloadLocator(Blockchain blockchain)
      {
        Blockchain = blockchain;
      }

      public UInt256 GetBlockHeaderHash()
      {
        if(!BlockLocationsQueued.Any())
        {
          BlockLocationsQueued = Blockchain.GetLocatorBatchBlocksUnassignedPayload(BatchSize);
          if(!BlockLocationsQueued.Any())
          {
            return null;
          }
        }
        
        do
        {
          UInt256 blockLocation = BlockLocationsQueued[0];
          BlockLocationsQueued.RemoveAt(0);

          if (!BlockLocationsDispatched.Any(b => b.IsEqual(blockLocation)))
          {
            BlockLocationsDispatched.Add(blockLocation);
            return blockLocation;
          }
        } while (BlockLocationsQueued.Any());

        return null;
      }

      public void RemoveDispatched(UInt256 hash)
      {
        BlockLocationsDispatched.RemoveAll(b => b.IsEqual(hash));
      }
    }
  }
}
