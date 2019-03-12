using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using BToken.Chaining;

namespace BToken.Accounting
{
  public partial class UTXO
  {
    partial class UTXOBuilder
    {
      class UTXOBatch
      {
        public int BatchIndex;
        public HeaderLocation[] HeaderLocations;
        public Block[] Blocks;


        public UTXOBatch(int batchIndex, HeaderLocation[] headerLocations)
        {
          BatchIndex = batchIndex;
          HeaderLocations = headerLocations;
          Blocks = new Block[headerLocations.Length];
        }   

      }
    }
  }
}
