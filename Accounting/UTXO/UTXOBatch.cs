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

      public int BlockCount;
      public int TXCount;

      public List<Block> Blocks = new List<Block>(50);

      const int AVERAGE_INPUTS_PER_TX = 5;
      public TXInput[] Inputs = new TXInput[COUNT_TXS_IN_BATCH_FILE * AVERAGE_INPUTS_PER_TX];
      public int IndexInputs;

      public UTXOIndexUInt32 TableUInt32 = new UTXOIndexUInt32();
      public KeyValuePair<byte[], uint>[] UTXOsUInt32;
      public UTXOIndexULong64 TableULong64 = new UTXOIndexULong64();
      public KeyValuePair<byte[], ulong>[] UTXOsULong64;
      public UTXOIndexUInt32Array TableUInt32Array = new UTXOIndexUInt32Array();
      public KeyValuePair<byte[], uint[]>[] UTXOsUInt32Array;

      public Headerchain.ChainHeader HeaderPrevious;
      public Headerchain.ChainHeader HeaderLast;

      public Stopwatch StopwatchParse = new Stopwatch();
    }
  }
}
