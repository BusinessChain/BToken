using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.Remoting.Metadata.W3cXsd2001;
using System.Threading.Tasks;

using BToken.Networking;

namespace BToken.Accounting.UTXO
{
  partial class UTXO
  {
    class UTXOTransaction
    {
      UTXO UTXO;
      UInt256 BlockHeaderHash;
      BitcoinTX CoinbaseTX;
      List<BitcoinTX> BitcoinTXs;

      //public Dictionary<UInt256, TXOutputsSpentMap> UnspentTXOutputs;
      public List<TXInput> SpendingTXInputs;


      public UTXOTransaction(List<BitcoinTX> bitcoinTXs, UInt256 blockHeaderHash)
      {
        BlockHeaderHash = blockHeaderHash;
        CoinbaseTX = bitcoinTXs.First();
        BitcoinTXs = bitcoinTXs.Skip(1).ToList();
      }

      public void Process()
      {
        ValidateTXOutputs(CoinbaseTX, CoinbaseTX.GetTXHash());

        foreach (BitcoinTX bitcoinTX in BitcoinTXs)
        {
          UInt256 tXHash = bitcoinTX.GetTXHash();

          try
          {
            ValidateTXOutputs(bitcoinTX, tXHash);
            ValidateTXInputsAsync(bitcoinTX, tXHash);
          }
          catch (Exception ex)
          {
            // Coinbase
            Undo(bitcoinTX);

            throw ex;
          }
        }
      }

      void ValidateTXOutputs(BitcoinTX bitcoinTX, UInt256 tXHash)
      {
        if (UTXO.UTXOTable.ContainsKey(tXHash.GetBytes()))
        {
          throw new UTXOException(
            string.Format("Ambiguous transactions '{0}' in block '{1}'", tXHash, BlockHeaderHash));
        }
        else
        {
          byte[] uTXOKey = CreateUTXOKeyValuePair(unspentTXOutput, out byte[] tXOutputsSpentByteMap);
          UTXO.UTXOTable.Add(uTXOKey, tXOutputsSpentByteMap);
          UnspentTXOutputs.Add(tXHash, new TXOutputsSpentMap(bitcoinTX.TXOutputs));
        }
      }
      byte[] CreateUTXOKeyValuePair(KeyValuePair<UInt256, TXOutputsSpentMap> unspentTXOutput, out byte[] uTXOValue)
      {
        byte[] tXIDBytes = unspentTXOutput.Key.GetBytes();
        byte[] outputSpentMapBytes = unspentTXOutput.Value.GetBytes();

        throw new NotImplementedException();
      }

      void ValidateTXInputsAsync(BitcoinTX bitcoinTX, UInt256 tXHash)
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
              string.Format("Validate tXInput '{0}' in transaction '{1}' in block '{2}' threw exception.",
              index, tXHash, BlockHeaderHash),
              ex);
          }
        }
      }
      async Task ValidateTXInputAsync(TXInput tXInput)
      {
        //if (UnspentTXOutputs.TryGetValue(tXInput.TXIDOutput, out TXOutputsSpentMap tXOutputsSpentMap))
        //{
        //  if (GetOutputSpentFlag(tXOutputsSpentMap.FlagsOutputsSpent, tXInput.IndexOutput))
        //  {
        //    throw new UTXOException(
        //      string.Format("Referenced output txid: '{0}', index: '{1}' is already spent in same block.",
        //      tXInput.TXIDOutput, tXInput.IndexOutput));
        //  }
        //  else
        //  {
        //    TXOutput tXOutput = tXOutputsSpentMap.TXOutputs[tXInput.IndexOutput];

        //    if (BitcoinScript.Evaluate(tXOutput.LockingScript, tXInput.UnlockingScript))
        //    {
        //      SetOutputSpentFlag(tXOutputsSpentMap.FlagsOutputsSpent, tXInput.IndexOutput);
        //    }
        //    else
        //    {
        //      throw new UTXOException(string.Format("Input script '{0}' failed to unlock output script '{1}'",
        //        new SoapHexBinary(tXInput.UnlockingScript),
        //        new SoapHexBinary(tXOutput.LockingScript)));
        //    }
        //  }
        //}
        var tXOuputLockingScript = await GetTXOutputLockingScriptAsync(tXInput);
        if (BitcoinScript.Evaluate(tXOuputLockingScript, tXInput.UnlockingScript))
        {
          SetOutputSpentFlag(tXOutputsSpentByteMap, tXInput.IndexOutput);
        }
        else
        {
          throw new UTXOException(string.Format("Input script '{0}' failed to unlock output script '{1}'",
            new SoapHexBinary(tXInput.UnlockingScript),
            new SoapHexBinary(tXOuputLockingScript)));
        }

        using (UTXOStream uTXOStream = new UTXOStream(UTXO, tXInput))
        {
          TXOutput tXOutput = uTXOStream.ReadTXOutput();

          while (tXOutput != null)
          {
            if (BitcoinScript.Evaluate(tXOutput.LockingScript, tXInput.UnlockingScript))
            {
              SetOutputSpentFlag(tXOutputsSpentByteMap, tXInput.IndexOutput);
              return;
            }
            tXOutput = uTXOStream.ReadTXOutput();
          }
        }
        
        SpendingTXInputs.Add(tXInput);
      }
      async Task<(int count, double sum)> GetTXOutputLockingScriptAsync(TXInput tXInput)
      {
        int count = 0;
        double sum = 0.0;
        foreach (var value in values)
        {
          count++;
          sum += value;
        }
        await Task.Delay(1);
        return (count, sum);
      }
    }
  }
}
