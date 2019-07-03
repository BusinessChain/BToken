using System.Diagnostics;
using System.Collections.Generic;
using System.Security.Cryptography;

using BToken.Chaining;

namespace BToken.Accounting
{
  public partial class UTXO
  {
    class UTXOBatch
    {
      public bool IsCancellationBatch;

      public int BatchIndex;
      public byte[] Buffer;
      public int BufferIndex;

      public List<Block> Blocks = new List<Block>(50);
      public List<UTXOParserData> UTXOParserData = new List<UTXOParserData>(50);

      public Stopwatch StopwatchMerging = new Stopwatch();
      public Stopwatch StopwatchParse = new Stopwatch();
    }
  }
}
