using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace BToken.Accounting.Bitcoin
{
  partial class UTXO
  {
    class UTXOTransaction
    {
      public Dictionary<string, string> UnspentOutputs = new Dictionary<string, string>();
      public Dictionary<string, string> SpentOutputs = new Dictionary<string, string>();


      public UTXOTransaction(List<BitcoinTX> bitcoinTXs, UInt256 blockHeaderHash)
      {
        foreach (BitcoinTX bitcoinTX in bitcoinTXs)
        {
          string txHashString = bitcoinTX.GetTXHash().ToString();

          SortTXOutputs(bitcoinTX.TXOutputs, txHashString, blockHeaderHash);
          SortTXInputs(bitcoinTX.TXInputs, txHashString, blockHeaderHash);
        }
      }
      void SortTXInputs(List<TXInput> txInputs, string txHashString, UInt256 blockHeaderHash)
      {
        for (int index = 0; index < txInputs.Count; index++)
        {
          TXInput txInput = txInputs[index];
          string outputReference = GetOutputReference(txInput.TXID.ToString(), txInput.IndexOutput);

          if (UnspentOutputs.ContainsKey(outputReference))
          {
            UnspentOutputs.Remove(outputReference);
          }
          else
          {
            SpentOutputs.Add(outputReference, blockHeaderHash.ToString());
          }
        }
      }
      void SortTXOutputs(List<TXOutput> txOutputs, string txHashString, UInt256 blockHeaderHash)
      {
        for (int index = 0; index < txOutputs.Count; index++)
        {
          string outputReference = GetOutputReference(txHashString, (uint)index);

          if (UnspentOutputs.ContainsKey(outputReference))
          {
            throw new UTXOException(string.Format("ambiguous output '{0}' in block '{1}'", outputReference, blockHeaderHash));
          }
          else
          {
            UnspentOutputs.Add(outputReference, blockHeaderHash.ToString());
          }
        }
      }

      static string GetOutputReference(string txid, uint index)
      {
        return txid + "." + index;
      }
    }
  }
}
