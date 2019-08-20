using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;


namespace BToken.Chaining
{
  public partial class Blockchain
  {
    class DownloadBatch
    {
      public bool IsAtTipOfChain = false;

      public int Index;

      public List<Header> Headers = new List<Header>();
      public List<Block> Blocks = new List<Block>();

      public long BytesDownloaded;


      public DownloadBatch(int batchIndex)
      {
        Index = batchIndex;
      }
    }
  }
}
