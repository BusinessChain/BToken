using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.Remoting.Metadata.W3cXsd2001;
using System.Threading.Tasks;

using BToken.Chaining;

namespace BToken.Accounting.UTXO
{
  partial class UTXO
  {
    class UTXOTransaction
    {
      UTXO UTXO;
      UInt256 BlockHeaderHash;
      TX CoinbaseTX;
      List<TX> TXs;
      UInt256[] TXHashes;


      public UTXOTransaction(List<TX> tXs, UInt256 blockHeaderHash)
      {
        BlockHeaderHash = blockHeaderHash;
        CoinbaseTX = tXs.First();
        TXs = tXs.Skip(1).ToList();
        TXHashes = new UInt256[TXs.Count];
      }

      public async Task ProcessAsync()
      {
        await InsertTXOutputsAsync(CoinbaseTX, CoinbaseTX.GetTXHash());

        for (int i = 0; i < TXs.Count; i++)
        {
          TXHashes[i] = TXs[i].GetTXHash();

          try
          {
            await InsertTXOutputsAsync(TXs[i], TXHashes[i]);
          }
          catch (Exception ex)
          {
            // Coinbase
            Undo(TXs[i]);

            throw ex;
          }
        }

        for (int i = 0; i < TXs.Count; i++)
        {
          try
          {
            await ValidateTXInputsAsync(TXs[i], TXHashes[i]);
          }
          catch (Exception ex)
          {
            // Coinbase
            Undo(TXs[i]);

            throw ex;
          }
        }
      }

      async Task InsertTXOutputsAsync(TX tX, UInt256 tXHash)
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

          byte[] headerIndex = new ArraySegment<byte>(tXOutputIndex, tXOutputIndex.Length - 3, 4).Array;
          if (await UTXO.ReadTXAsync(tXHash, headerIndex) != null)
          {
            throw new UTXOException(
              string.Format("Ambiguous transaction '{0}' in block '{1}' and block header hash index '{2}'",
              tXHash,
              BlockHeaderHash,
              new SoapHexBinary(headerIndex)));
          }

          numberOfKeyBytes++;
          uTXOKey = tXHashBytes.Take(numberOfKeyBytes).ToArray();
        }

        byte[] bitMapTXOutputsSpent = new byte[(tX.TXOutputs.Count + 7) / 8];
        UTXO.UTXOTable.Add(uTXOKey, bitMapTXOutputsSpent);
      }

      async Task ValidateTXInputsAsync(TX tX, UInt256 tXHash)
      {
        for (int index = 0; index < tX.TXInputs.Count; index++)
        {
          TXInput tXInput = tX.TXInputs[index];

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
        byte[] tXHashBytes = tXInput.TXIDOutput.GetBytes();
        int numberOfKeyBytes = 4;
        byte[] uTXOKey = tXHashBytes.Take(numberOfKeyBytes).ToArray();

        while (UTXO.UTXOTable.TryGetValue(uTXOKey, out byte[] tXOutputIndex))
        {
          byte[] blockHeaderHashIndex = new ArraySegment<byte>(tXOutputIndex, tXOutputIndex.Length - 3, 4).Array;
          TX tX = await UTXO.ReadTXAsync(tXInput.TXIDOutput, blockHeaderHashIndex);
          if (tX == null)
          {
            numberOfKeyBytes++;
            uTXOKey = tXHashBytes.Take(numberOfKeyBytes).ToArray();
            continue;
          }
          else
          {
            byte[] bitMapTXOutputsSpent = new ArraySegment<byte>(tXOutputIndex, 0, tXOutputIndex.Length - 4).Array;
            if (IsOutputSpent(bitMapTXOutputsSpent, tXInput.IndexOutput))
            {
              throw new UTXOException(string.Format("TXInput references spent output TXID: '{0}', index: '{1}', block index: '{2}'",
                tXInput.TXIDOutput, 
                tXInput.IndexOutput,
                new SoapHexBinary(uTXOKey)));
            }

            TXOutput tXOutput = tX.TXOutputs[tXInput.IndexOutput];
            if (Script.Evaluate(tXOutput.LockingScript, tXInput.UnlockingScript))
            {
              SpendTXOutput(bitMapTXOutputsSpent, tXInput.IndexOutput);
            }
            else
            {
              throw new UTXOException(string.Format("Input script '{0}' failed to unlock output script '{1}'",
                new SoapHexBinary(tXInput.UnlockingScript),
                new SoapHexBinary(tXOutput.LockingScript)));
            }

            if(AreAllOutputsSpent(bitMapTXOutputsSpent))
            {
              UTXO.UTXOTable.Remove(uTXOKey);
            }
          }
        }

        throw new UTXOException(string.Format("TXInput references spent or nonexistant output TXID: '{0}', index: '{1}'",
          tXInput.TXIDOutput, tXInput.IndexOutput));
      }
      static bool AreAllOutputsSpent(byte[] bitMapTXOutputsSpent)
      {
        for(int i = 0; i < bitMapTXOutputsSpent.Length; i++)
        {
          if (bitMapTXOutputsSpent[i] != 0x00) { return false; }
        }

        return true;
      }
      static void SpendTXOutput(byte[] bitMapTXOutputsSpent, int index)
      {
        int byteIndex = index / 8;
        int bitIndex = index % 8;
        bitMapTXOutputsSpent[byteIndex] |= (byte)(0x01 << bitIndex);
      }
    }
  }
}
