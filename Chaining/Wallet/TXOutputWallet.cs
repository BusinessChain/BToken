using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace BToken.Chaining
{
  partial class UTXOTable
  {
    class TXOutputWallet
    {
      public byte[] TXID;
      public int TXIDShort;
      public int OutputIndex;
      public ulong Value;
    }
  }
}
