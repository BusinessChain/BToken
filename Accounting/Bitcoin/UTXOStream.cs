using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;


namespace BToken.Accounting.Bitcoin
{
  partial class UTXO
  {
    class UTXOStream : IDisposable
    {
      byte[] TXID;
      byte[] BlockHeaderHash;
      byte[] Position;

      public UTXOStream(byte[] tXID, byte[] blockHeaderHash, byte[] position)
      {
        TXID = tXID;
        BlockHeaderHash = blockHeaderHash;
        Position = position;
      }

      public TXOutput ReadTXOutput()
      {

      }

      public void Dispose()
      {

      }
    }
  }
}
