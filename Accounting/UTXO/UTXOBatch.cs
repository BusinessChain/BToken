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
      public int BatchIndex;

      public byte[] Buffer;
      public int BufferIndex;

      public List<Block> Blocks = new List<Block>(50);

      public byte[] HeaderHashPrevious;


      public Headerchain.ChainHeader ChainHeader;
      public SHA256 SHA256 = SHA256.Create();

      public Stopwatch StopwatchMerging = new Stopwatch();
      public Stopwatch StopwatchResolver = new Stopwatch();
      public Stopwatch StopwatchParse = new Stopwatch();
      
    }
  }
}
