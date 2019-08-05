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
    partial class UTXONetworkLoader
    {
      class UTXODownloadBatch
      {
        public bool IsCancellationBatch = false;

        public int BatchIndex;

        public List<Headerchain.ChainHeader> Headers = new List<Headerchain.ChainHeader>(COUNT_BLOCKS_DOWNLOAD_BATCH);
        public List<Block> Blocks = new List<Block>(COUNT_BLOCKS_DOWNLOAD_BATCH);

        public long BytesDownloaded;


        public UTXODownloadBatch(int batchIndex)
        {
          BatchIndex = batchIndex;
        }
      }
    }
  }
}
