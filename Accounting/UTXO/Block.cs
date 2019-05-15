using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace BToken.Accounting
{
  public partial class UTXO
  {
    class Block
    {
      public byte[] BlockBytes;
      public byte[] HeaderHash;
      public TX[] TXs;

      public Block()
      { }
    }
  }
}
