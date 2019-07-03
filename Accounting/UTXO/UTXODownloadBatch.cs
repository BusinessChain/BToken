using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Security.Cryptography;


namespace BToken.Accounting
{
  public partial class UTXO
  {
    partial class UTXOBuilder
    {
      class UTXODownloadBatch
      {
        public bool IsCancellationBatch = false;

        public int BatchIndex;

        public List<byte[]> HeaderHashes = new List<byte[]>(COUNT_BLOCKS_DOWNLOAD_BATCH);
        public List<Block> Blocks = new List<Block>(COUNT_BLOCKS_DOWNLOAD_BATCH);

        public UTXODownloadBatch(int batchIndex)
        {
          BatchIndex = batchIndex;
        }
      }
    }
  }
}
