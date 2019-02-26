using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.Remoting.Metadata.W3cXsd2001;
using System.Threading.Tasks;

using BToken.Chaining;

namespace BToken.Accounting
{
  public partial class UTXO
  {
    partial class UTXOBuilder
    {
      class UTXOBuilderBatch
      {
        UTXO UTXO;
        UTXOBuilder UTXOBuilder;

        List<HeaderLocation> HeaderLocations;
        public Dictionary<byte[], List<TXInput>> InputsUnfunded;
        public Dictionary<byte[], byte[]> UTXOs;
        public int BatchIndex;

        public UTXOBuilderBatch(
          UTXO uTXO, 
          UTXOBuilder uTXOBuilder,
          List<HeaderLocation> headerLocations,
          int batchIndex)
        {
          UTXO = uTXO;
          UTXOBuilder = uTXOBuilder;
          HeaderLocations = headerLocations;
          InputsUnfunded = new Dictionary<byte[], List<TXInput>>(new EqualityComparerByteArray());
          UTXOs = new Dictionary<byte[], byte[]>(new EqualityComparerByteArray());
          BatchIndex = batchIndex;
        }

        public async Task BuildAsync()
        {
          foreach (HeaderLocation headerLocation in HeaderLocations)
          {
            Block block = await UTXO.GetBlockAsync(headerLocation.Hash);

            UTXOTransaction.BuildBlock(block, InputsUnfunded, UTXOs);
          }

          await UTXOBuilder.MergeBatchAsync(this);
        }

      }
    }
  }
}
