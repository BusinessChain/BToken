using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace BToken.Chaining
{
  partial class UTXOTable
  {
    partial class WalletUTXO
    {
      class TXOutputWallet
      {
        public byte[] TXID = new byte[HASH_BYTE_SIZE];
        public int TXIDShort;
        public int OutputIndex;
        public ulong Value;
        public byte[] ScriptPubKey = new byte[LENGTH_P2PKH];
      }
    }
  }
}
