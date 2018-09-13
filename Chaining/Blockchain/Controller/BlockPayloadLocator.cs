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
      UInt256 BlockLocationRequestedLast;


      public BlockPayloadLocator(Blockchain blockchain)
      {
        Blockchain = blockchain;
      }

      public List<UInt256> GetBlockLocations()
      {
        throw new NotImplementedException();
      }
    }
  }
}
