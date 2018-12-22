using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using BToken.Networking;

namespace BToken.Accounting.Bitcoin
{
  partial class UTXO
  {
    class UTXOTransaction
    {
      class TXOutputsSpentMap
      {
        public List<TXOutput> TXOutputs;
        public BitArray Flags;

        public TXOutputsSpentMap(List<TXOutput> tXOutputs)
        {
          TXOutputs = tXOutputs;
          Flags = new BitArray(tXOutputs.Count);
        }
      }

      UTXO UTXO;
      UInt256 BlockHeaderHash;

      Dictionary<UInt256, TXOutputsSpentMap> UnspentTXOutputs = new Dictionary<UInt256, TXOutputsSpentMap>();
      public Dictionary<string, TXOutput> TXOutputs = new Dictionary<string, TXOutput>();
      public Dictionary<string, TXInput> TXInputs = new Dictionary<string, TXInput>();


      public UTXOTransaction(UTXO uTXO, List<BitcoinTX> bitcoinTXs, UInt256 blockHeaderHash)
      {
        UTXO = uTXO;
        BlockHeaderHash = blockHeaderHash;

        BitcoinTX coinbaseTX = bitcoinTXs.First();
        bitcoinTXs.Remove(coinbaseTX);
        ValidateCoinbaseTX(coinbaseTX);
        ValidateTXOutputs(coinbaseTX);

        bitcoinTXs.ForEach(b => ValidateTXOutputs(b));
        bitcoinTXs.ForEach(b => ValidateTXInputsAsync(b));

        foreach (KeyValuePair<string, TXOutput> tXOutput in TXOutputs)
        {
          UTXO.TXOutputs.Add(tXOutput.Key, BlockHeaderHash);
        }
      }
      void ValidateCoinbaseTX(BitcoinTX coinbaseTX)
      {
        //  return GetOutputReference(txInput) == "0000000000000000000000000000000000000000000000000000000000000000.4294967295";
      }

      void ValidateTXOutputs(BitcoinTX bitcoinTX)
      {
        UInt256 tXHash = bitcoinTX.GetTXHash();

        if (UnspentTXOutputs.ContainsKey(tXHash))
        {
          throw new UTXOException(
            string.Format("Ambiguous transaction '{0}' in block '{1}'", tXHash, BlockHeaderHash));
        }
        else
        {
          UnspentTXOutputs.Add(tXHash, new TXOutputsSpentMap(bitcoinTX.TXOutputs));
        }
      }

      void ValidateTXInputsAsync(BitcoinTX bitcoinTX)
      {
        for (int index = 0; index < bitcoinTX.TXInputs.Count; index++)
        {
          TXInput tXInput = bitcoinTX.TXInputs[index];

          try
          {
            ValidateTXInputAsync(tXInput);
          }
          catch (UTXOException ex)
          {
            throw new UTXOException(
              string.Format("Validate tXInput '{0}' in TX '{1}' in block '{2}' threw exception.", index, bitcoinTX.GetTXHash(), BlockHeaderHash),
              ex);
          }
        }
      }
      void ValidateTXInputAsync(TXInput tXInput)
      {
        if (UnspentTXOutputs.TryGetValue(tXInput.TXID, out TXOutputsSpentMap tXOutputsSpentMap))
        {
          if (tXOutputsSpentMap.Flags[(int)tXInput.IndexOutput])
          {
            throw new UTXOException(
              string.Format("Referenced output txid: '{0}', index: '{1}' is already spent in same block.", 
              tXInput.TXID, tXInput.IndexOutput));
          }
          else
          {
            TXOutput tXOutput = tXOutputsSpentMap.TXOutputs[(int)tXInput.IndexOutput];
            tXOutput.UnlockScript(tXInput.UnlockingScript);
            tXOutputsSpentMap.Flags[(int)tXInput.IndexOutput] = true;
          }
        }
        else if (UTXO.UnspentTXOutputs.TryGetValue(tXInput.TXID, out byte[] tXOutputIndex))
        {
          NetworkBlock blockReferenced = await UTXO.Blockchain.GetBlockAsync(headerHashReferenced);
          tXOutput = GetTXOutput(blockReferenced, tXInput);
          tXOutput.UnlockScript(tXInput.UnlockingScript);

          UTXO.TXOutputs.Remove(outputReference);
        }
        else
        {
          throw new UTXOException(string.Format("TXInput references spent or nonexistant output TXID: '{0}', index: '{1}'",
            tXInput.TXID, tXInput.IndexOutput));
        }
      }

      TXOutput GetTXOutput(NetworkBlock blockReferenced, TXInput txInput)
      {
        List<BitcoinTX> bitcoinTXs = UTXO.PayloadParser.Parse(blockReferenced.Payload);

        BitcoinTX bitcoinTX = bitcoinTXs.Find(b => b.GetTXHash().IsEqual(txInput.TXID));
        return bitcoinTX.TXOutputs[(int)txInput.IndexOutput];
      }
    }
  }
}
