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
        public int Index;
        public byte[][] HeaderHashes;
        public List<Block> Blocks;

        public UTXODownloadBatch()
        {
          HeaderHashes = new byte[COUNT_BLOCKS_DOWNLOAD][];
          Blocks = new List<Block>(COUNT_BLOCKS_DOWNLOAD);
        }
      }
    }
  }
}
