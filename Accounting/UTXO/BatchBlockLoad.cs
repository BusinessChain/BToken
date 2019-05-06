using System;
using System.Diagnostics;
using System.IO;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Security.Cryptography;

using BToken.Networking;
using BToken.Chaining;

namespace BToken.Accounting
{
  public partial class UTXO
  {
    class BatchBlockLoad
    {
      public int BatchIndex;
      public List<Block> Blocks = new List<Block>();
      public Headerchain.ChainHeader ChainHeader;
      public SHA256 SHA256Generator = SHA256.Create();


      public BatchBlockLoad(int batchIndex)
      {
        BatchIndex = batchIndex;
      }
    }
  }
}
