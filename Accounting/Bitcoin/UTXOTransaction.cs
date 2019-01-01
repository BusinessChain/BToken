using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.Remoting.Metadata.W3cXsd2001;
using System.Threading.Tasks;

using BToken.Networking;

namespace BToken.Accounting.Bitcoin
{
  partial class UTXO
  {
    class UTXOTransaction
    {
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
        if (UnspentTXOutputs.ContainsKey(tXHash)|| UTXO.UnspentTXOutputs.ContainsKey(tXHash))
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
          if (GetOutputSpentFlag(tXOutputsSpentMap.FlagsOutputsSpent, tXInput.IndexOutput))
          {
            throw new UTXOException(
              string.Format("Referenced output txid: '{0}', index: '{1}' is already spent in same block.",
              tXInput.TXIDOutput, tXInput.IndexOutput));
          }
          else
          {
            TXOutput tXOutput = tXOutputsSpentMap.TXOutputs[tXInput.IndexOutput];

            if (BitcoinScript.Evaluate(tXOutput.LockingScript, tXInput.UnlockingScript))
            {
              SetOutputSpentFlag(tXOutputsSpentMap.FlagsOutputsSpent, tXInput.IndexOutput);
            }
            else
            {
              throw new UTXOException(string.Format("Input script '{0}' failed to unlock output script '{1}'",
                new SoapHexBinary(tXInput.UnlockingScript).ToString(),
                new SoapHexBinary(tXOutput.LockingScript).ToString()));
            }
          }
        }
        else if (UTXO.UnspentTXOutputs.TryGetValue(tXInput.TXIDOutput, out byte[] tXOutputIndex))
        {
          byte[] tXOutputsSpentByteMap = new ArraySegment<byte>(tXOutputIndex, 2, tXOutputIndex.Length - 8).Array;
          if (GetOutputSpentFlag(tXOutputsSpentByteMap, tXInput.IndexOutput))
          {
            throw new UTXOException(
              string.Format("Referenced output txid: '{0}', index: '{1}' is already spent.",
              tXInput.TXIDOutput, tXInput.IndexOutput));
          }

          byte[] tXID = new ArraySegment<byte>(tXOutputIndex, 0, 2).Array;
          byte[] blockHeaderHash = new ArraySegment<byte>(tXOutputIndex, tXOutputIndex.Length - 8, 4).Array;
          byte[] position = new ArraySegment<byte>(tXOutputIndex, tXOutputIndex.Length - 4, 4).Array;
          using (UTXOStream uTXOStream = new UTXOStream(tXID, blockHeaderHash, position))
          {
            TXOutput tXOutput = uTXOStream.ReadTXOutput();

            while (tXOutput != null)
            {
              if(BitcoinScript.Evaluate(tXOutput.LockingScript, tXInput.UnlockingScript))
              {
                SetOutputSpentFlag(tXOutputsSpentByteMap, tXInput.IndexOutput);
                return;
              }
              tXOutput = uTXOStream.ReadTXOutput();
            }
          }

          throw new UTXOException(string.Format("TXInput references spent or nonexistant output TXID: '{0}', index: '{1}'",
            tXInput.TXIDOutput, tXInput.IndexOutput));
        }
      }

      static void SetOutputSpentFlag(byte[] flagsOutputsSpent, int index)
      {
        int byteIndex = index / 8;
        int bitIndex = index % 8;
        flagsOutputsSpent[byteIndex] |= (byte)(0x01 << bitIndex);
      }
      static bool GetOutputSpentFlag(byte[] flagsOutputsSpent, int index)
      {
        int byteIndex = index / 8;
        int bitIndex = index % 8;
        byte maskFlag = (byte)(0x01 << bitIndex);
        return (maskFlag & flagsOutputsSpent[byteIndex]) != 0x00;
      }

      TXOutput GetTXOutput(NetworkBlock blockReferenced, TXInput txInput)
      {
        List<BitcoinTX> bitcoinTXs = UTXO.PayloadParser.Parse(blockReferenced.Payload);

        BitcoinTX bitcoinTX = bitcoinTXs.Find(b => b.GetTXHash().IsEqual(txInput.TXIDOutput));
        return bitcoinTX.TXOutputs[txInput.IndexOutput];
      }
    }
  }
}
