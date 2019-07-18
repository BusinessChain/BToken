using System.Diagnostics;
using System.Collections.Generic;

using BToken.Chaining;

namespace BToken.Accounting
{
  public partial class UTXO
  {
    class UTXOBatch
    {
      public bool IsCancellationBatch;

      public int BatchIndex;

      public int TXCount;
      public List<Block> Blocks = new List<Block>(50);
      public List<UTXOParserData> UTXOParserDatasets = new List<UTXOParserData>(50);

      public Headerchain.ChainHeader HeaderPrevious;
      public Headerchain.ChainHeader HeaderLast;

      public Stopwatch StopwatchParse = new Stopwatch();

    }
  }
}
