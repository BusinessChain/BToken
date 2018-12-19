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
      UInt256 BlockHeaderHash;

      public Dictionary<string, TXOutput> TXOutputs = new Dictionary<string, TXOutput>();
      public Dictionary<string, TXInput> TXInputs = new Dictionary<string, TXInput>();


      public UTXOTransaction(List<BitcoinTX> bitcoinTXs, UInt256 blockHeaderHash)
      {
        BlockHeaderHash = blockHeaderHash;

        BitcoinTX coinbaseTX = bitcoinTXs.First();
        bitcoinTXs.Remove(coinbaseTX);
        ValidateCoinbaseTX(coinbaseTX);
        FilterTXOutputs(coinbaseTX.TXOutputs, coinbaseTX.GetTXHash());
        
        foreach (BitcoinTX bitcoinTX in bitcoinTXs)
        {
          UInt256 txHash = bitcoinTX.GetTXHash();

          FilterTXOutputs(bitcoinTX.TXOutputs, txHash);
          FilterTXInputs(bitcoinTX.TXInputs, txHash);
        }
      }
      void ValidateCoinbaseTX(BitcoinTX coinbaseTX)
      {

      }
      //bool IsCoinbase(TXInput txInput)
      //{
      //  return GetOutputReference(txInput) == "0000000000000000000000000000000000000000000000000000000000000000.4294967295";
      //}

      void FilterTXOutputs(List<TXOutput> tXOutputs, UInt256 txHash)
      {
        for (int index = 0; index < tXOutputs.Count; index++)
        {
          TXOutput tXOutput = tXOutputs[index];
          string outputReference = GetOutputReference(txHash, (uint)index);

          if (TXOutputs.ContainsKey(outputReference))
          {
            throw new UTXOException(string.Format("ambiguous output '{0}' in block '{1}'",
              outputReference, BlockHeaderHash));
          }
          else
          {
            TXOutputs.Add(outputReference, tXOutput);
          }
        }
      }
      void FilterTXInputs(List<TXInput> txInputs, UInt256 txHash)
      {
        for (int index = 0; index < txInputs.Count; index++)
        {
          TXInput txInput = txInputs[index];
          string outputReference = GetOutputReference(txInput);

          if (TXOutputs.TryGetValue(outputReference, out TXOutput tXOutput))
          {
            if (tXOutput.TryUnlockScript(txInput.UnlockingScript))
            {
              TXOutputs.Remove(outputReference);
            }
            else
            {
              throw new UTXOException(string.Format("Invalid txInput '{0}' in tx '{1}' in block '{2}'",
                index, txHash, BlockHeaderHash));
            }
          }
          else
          {
            TXInputs.Add(outputReference, txInput);
          }
        }
      }
    }
  }
}
