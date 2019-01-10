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
      TX CoinbaseTX;
      List<TX> BitcoinTXs;
      UInt256[] TXHashes;
    

      public UTXOTransaction(List<TX> bitcoinTXs, UInt256 blockHeaderHash)
      {
        BlockHeaderHash = blockHeaderHash;
        CoinbaseTX = bitcoinTXs.First();
        BitcoinTXs = bitcoinTXs.Skip(1).ToList();
        TXHashes = new UInt256[BitcoinTXs.Count];
      }

      public async Task ProcessAsync()
      {
        ParseTXOutputs(CoinbaseTX, CoinbaseTX.GetTXHash());

        for(int i = 0; i < BitcoinTXs.Count; i++)
        {
          TXHashes[i] = BitcoinTXs[i].GetTXHash();

          try
          {
            ParseTXOutputs(BitcoinTXs[i], TXHashes[i]);
          }
          catch (Exception ex)
          {
            // Coinbase
            Undo(BitcoinTXs[i]);

            throw ex;
          }
        }

        for (int i = 0; i < BitcoinTXs.Count; i++)
        {
          try
          {
            await ValidateTXInputsAsync(BitcoinTXs[i], TXHashes[i]);
          }
          catch (Exception ex)
          {
            // Coinbase
            Undo(BitcoinTXs[i]);

            throw ex;
          }
        }
      }

      void ParseTXOutputs(TX bitcoinTX, UInt256 tXHash)
      {
        byte[] tXHashBytes = tXHash.GetBytes();
        int numberOfKeyBytes = 4;
        byte[] uTXOKey = tXHashBytes.Take(numberOfKeyBytes).ToArray();

        while (UTXO.UTXOTable.TryGetValue(uTXOKey, out byte[] tXOutputIndex))
        {
          if (numberOfKeyBytes == tXHashBytes.Length)
          {
            throw new UTXOException(
              string.Format("Ambiguous transaction '{0}' in block '{1}'", tXHash, BlockHeaderHash));
          }

          byte[] headerHashKey = new ArraySegment<byte>(tXOutputIndex, tXOutputIndex.Length - 3, 4).Array;
          using (TXStream tXStream = new TXStream(headerHashKey))
          {
            TX bitcoinTXExisting = tXStream.ReadTX();

            while(bitcoinTXExisting != null)
            {
              if (tXHash.IsEqual(bitcoinTXExisting.GetTXHash()))
              {
                throw new UTXOException(
                  string.Format("Ambiguous transaction '{0}' in block '{1}'", tXHash, BlockHeaderHash));
              }

              bitcoinTXExisting = tXStream.ReadTX();
            }
          }

          uTXOKey = tXHashBytes.Take(++numberOfKeyBytes).ToArray();
        }

        byte[] bitMapTXOutputsSpent = new byte[(bitcoinTX.TXOutputs.Count + 7) / 8];
        UTXO.UTXOTable.Add(uTXOKey, bitMapTXOutputsSpent);
      }
      
      async Task ValidateTXInputsAsync(TX bitcoinTX, UInt256 tXHash)
      {
        for (int index = 0; index < bitcoinTX.TXInputs.Count; index++)
        {
          TXInput tXInput = bitcoinTX.TXInputs[index];

          try
          {
            await ValidateTXInputAsync(tXInput);
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
        var tupleTXOutput = await GetTupleTXOutputAsync(tXInput);
        if (Script.Evaluate(tupleTXOutput.lockingScript, tXInput.UnlockingScript))
        {
          SpendTXOutput(tupleTXOutput.bitMapTXOutputsSpent, tXInput.IndexOutput);
        }
        else
        {
          throw new UTXOException(string.Format("Input script '{0}' failed to unlock output script '{1}'",
            new SoapHexBinary(tXInput.UnlockingScript),
            new SoapHexBinary(tupleTXOutput.lockingScript)));
        }
      }

      static void SpendTXOutput(byte[] bitMapTXOutputsSpent, int index)
      {
        int byteIndex = index / 8;
        int bitIndex = index % 8;
        bitMapTXOutputsSpent[byteIndex] |= (byte)(0x01 << bitIndex);
      }

      async Task<(byte[] lockingScript, byte[] bitMapTXOutputsSpent)> GetTupleTXOutputAsync(TXInput tXInput)
      {
        using (UTXOStream uTXOStream = new UTXOStream(UTXO, tXInput))
        {
          TXOutput tXOutput = uTXOStream.ReadTXOutput();

          while (tXOutput != null)
          {
            if (Script.Evaluate(tXOutput.LockingScript, tXInput.UnlockingScript))
            {
              SetOutputSpentFlag(tXOutputsSpentByteMap, tXInput.IndexOutput);
              return;
            }
            tXOutput = uTXOStream.ReadTXOutput();
          }
        }

        byte[] lockingScript = null;
        byte[] spentBitMap = null;
        
        await Task.Delay(1);
        return (lockingScript, spentBitMap);
      }
    }
  }
}
