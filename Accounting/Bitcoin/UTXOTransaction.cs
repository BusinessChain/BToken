using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using BToken.Networking;
using BToken.Chaining;

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


      public UTXOTransaction(UTXO uTXO, List<BitcoinTX> bitcoinTXs, UInt256 blockHeaderHash)
      {
        UTXO = uTXO;
        BlockHeaderHash = blockHeaderHash;

        BitcoinTX coinbaseTX = bitcoinTXs.First();
        ValidateCoinbaseTX(coinbaseTX);
        bitcoinTXs.Remove(coinbaseTX);

        foreach(BitcoinTX bitcoinTX in bitcoinTXs)
        {
          UInt256 tXHash = bitcoinTX.GetTXHash();

          try
          {
            ValidateTXOutputs(bitcoinTX.TXOutputs, tXHash);
          }
          catch (UTXOException ex)
          {
            throw new UTXOException(
              string.Format("Validating outputs in transaction '{0}' in block '{1}' threw exception.", 
                tXHash, BlockHeaderHash),
              ex);
          }
        }

        foreach (BitcoinTX bitcoinTX in bitcoinTXs)
        {
          UInt256 tXHash = bitcoinTX.GetTXHash();

          try
          {
            ValidateTXInputsAsync(bitcoinTX.TXInputs);
          }
          catch (UTXOException ex)
          {
            throw new UTXOException(
              string.Format("Validating inputs in transaction '{0}' in block '{1}' threw exception.",
                tXHash, BlockHeaderHash),
              ex);
          }
        }

      }
      void ValidateCoinbaseTX(BitcoinTX coinbaseTX)
      {
        ValidateTXOutputs(coinbaseTX.TXOutputs, coinbaseTX.GetTXHash());
        //  return GetOutputReference(txInput) == "0000000000000000000000000000000000000000000000000000000000000000.4294967295";
      }

      void ValidateTXOutputs(List<TXOutput> tXOutputs, UInt256 tXHash)
      {
        if (UnspentTXOutputs.ContainsKey(tXHash))
        {
          throw new UTXOException(
            string.Format("Ambiguous transaction '{0}' in block '{1}'", tXHash, BlockHeaderHash));
        }
        else
        {
          UnspentTXOutputs.Add(tXHash, new TXOutputsSpentMap(tXOutputs));
        }
      }

      void ValidateTXInputsAsync(List<TXInput> tXInputs)
      {
        for (int index = 0; index < tXInputs.Count; index++)
        {
          TXInput tXInput = tXInputs[index];

          try
          {
            ValidateTXInputAsync(tXInput);
          }
          catch (UTXOException ex)
          {
            throw new UTXOException(
              string.Format("Validate tXInput '{0}' threw exception.", index),
              ex);
          }
        }

      }
      void ValidateTXInputAsync(TXInput tXInput)
      {
        if (UnspentTXOutputs.TryGetValue(tXInput.TXIDOutput, out TXOutputsSpentMap tXOutputsSpentMap))
        {
          if (tXOutputsSpentMap.Flags[(int)tXInput.IndexOutput])
          {
            throw new UTXOException(
              string.Format("Referenced output txid: '{0}', index: '{1}' is already spent in same block.",
              tXInput.TXIDOutput, tXInput.IndexOutput));
          }
          else
          {
            TXOutput tXOutput = tXOutputsSpentMap.TXOutputs[(int)tXInput.IndexOutput];
            tXOutput.UnlockScript(tXInput.UnlockingScript);
            tXOutputsSpentMap.Flags[(int)tXInput.IndexOutput] = true;
          }
        }
        else if (UTXO.UnspentTXOutputs.TryGetValue(tXInput.TXIDOutput, out byte[] tXOutputIndex))
        {
          byte[] tXID = new ArraySegment<byte>(tXOutputIndex, 0, 2).Array;

          UTXOStream uTXOStream = new UTXOStream(tXID);
          var blockReferenced = await UTXO.Blockchain.GetBlockAsync(blockHeaderHashBytes);
          tXOutput = GetTXOutput(blockReferenced, tXInput);
          tXOutput.UnlockScript(tXInput.UnlockingScript);

          UTXO.TXOutputs.Remove(outputReference);
        }
        else
        {
          throw new UTXOException(string.Format("TXInput references spent or nonexistant output TXID: '{0}', index: '{1}'",
            tXInput.TXIDOutput, tXInput.IndexOutput));
        }

      }

      TXOutput GetTXOutput(NetworkBlock blockReferenced, TXInput txInput)
      {
        List<BitcoinTX> bitcoinTXs = UTXO.PayloadParser.Parse(blockReferenced.Payload);

        BitcoinTX bitcoinTX = bitcoinTXs.Find(b => b.GetTXHash().IsEqual(txInput.TXIDOutput));
        return bitcoinTX.TXOutputs[(int)txInput.IndexOutput];
      }
    }
  }
}
