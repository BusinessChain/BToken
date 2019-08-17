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
    class DownloadBatch
    {
      public bool IsAtTipOfChain = false;

      public int Index;

      public List<Headerchain.ChainHeader> Headers = new List<Headerchain.ChainHeader>();
      public List<Block> Blocks = new List<Block>();

      public long BytesDownloaded;


      public DownloadBatch(int batchIndex)
      {
        Index = batchIndex;
      }
    }
  }
}
