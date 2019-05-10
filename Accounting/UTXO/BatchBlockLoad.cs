using System;
using System.Diagnostics;
using System.IO;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Security.Cryptography;

using BToken.Chaining;

namespace BToken.Accounting
{
  public partial class UTXO
  {
    class BatchBlockLoad
    {
      public int Index;
      public byte[] Buffer;
      public readonly TaskCompletionSource<BatchBlockLoad> SignalBatchCompletion = new TaskCompletionSource<BatchBlockLoad>();
      public List<Block> Blocks = new List<Block>();
      public Headerchain.ChainHeader ChainHeader;
      public SHA256 SHA256Generator = SHA256.Create();

      public Stopwatch StopwatchMerging = new Stopwatch();
      public Stopwatch StopwatchParse = new Stopwatch();


      public BatchBlockLoad() { }
      public BatchBlockLoad(int batchindex) { }
    }
  }
}
