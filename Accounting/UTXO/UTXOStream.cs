using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;


namespace BToken.Accounting.UTXO
{
  partial class UTXO
  {
    class UTXOStream : IDisposable
    {
      UTXO UTXO;
      TXInput TXInput;
      byte[] BlockHeaderHash;

      public UTXOStream(UTXO uTXO, TXInput tXInput)
      {
        UTXO = uTXO;
        TXInput = tXInput;
        BlockHeaderHash = GetBlockHeaderHashKey(tXInput);
      }

      public TXOutput ReadTXOutput()
      {
        throw new NotImplementedException();
      }

      byte[] GetBlockHeaderHashKey(TXInput tXInput)
      {
        int numberOfKeyBytes = 4;
        var tXIDOutputBytes = tXInput.TXIDOutput.GetBytes();
        byte[] uTXOKey = tXIDOutputBytes.Take(numberOfKeyBytes).ToArray();

        while (UTXO.UTXOTable.TryGetValue(uTXOKey, out byte[] tXOutputIndex))
        {
          byte[] bitMapTXOutputsSpent = new ArraySegment<byte>(tXOutputIndex, 0, tXOutputIndex.Length - 4).Array;
          if (IsOutputSpent(bitMapTXOutputsSpent, tXInput.IndexOutput))
          {
            uTXOKey = tXIDOutputBytes.Take(++numberOfKeyBytes).ToArray();
            continue;
          }

          return new ArraySegment<byte>(tXOutputIndex, tXOutputIndex.Length - 3, 4).Array;
        }

        throw new UTXOException(string.Format("TXInput references spent or nonexistant output TXID: '{0}', index: '{1}'",
          tXInput.TXIDOutput, tXInput.IndexOutput));
      }

      public void Dispose()
      {

      }
    }
  }
}
