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
      UInt256 HeaderHash;
      TX CoinbaseTX;
      UInt256 HashCoinbaseTX;
      List<TX> TXs;
      UInt256[] HashesTX;

      int NumberIndexKeyBytesMin = 4;
      int NumberHeaderIndexBytes = 4;


      public UTXOTransaction(UTXO uTXO, List<TX> tXs, UInt256 blockHeaderHash)
      {
        UTXO = uTXO;
        HeaderHash = blockHeaderHash;
        CoinbaseTX = tXs.First();
        HashCoinbaseTX = CoinbaseTX.GetTXHash();
        TXs = tXs.Skip(1).ToList();
        HashesTX = new UInt256[TXs.Count];
      }

      public async Task InsertAsync()
      {
        byte[] uTXOKey = await GetUTXOKeyFreeAsync(HashCoinbaseTX);
        byte[] uTXOIndex = CreateUTXOIndex(CoinbaseTX.TXOutputs.Count);
        UTXO.UTXOTable.Add(uTXOKey, uTXOIndex);

        for (int i = 0; i < TXs.Count; i++)
        {
          HashesTX[i] = TXs[i].GetTXHash();

          try
          {
            uTXOKey = await GetUTXOKeyFreeAsync(HashesTX[i]);
          }
          catch (UTXOException ex)
          {
            await RemoveTXOutputIndexAsync(HashCoinbaseTX);
            await RemoveTXOutputIndexesAsync(stopTX: TXs[i]);
            throw ex;
          }

          uTXOIndex = CreateUTXOIndex(TXs[i].TXOutputs.Count);
          UTXO.UTXOTable.Add(uTXOKey, uTXOIndex);
        }
        for (int i = 0; i < TXs.Count; i++)
        {
          try
          {
            await SpendTXOutputsReferencedAsync(TXs[i], HashesTX[i]);
          }
          catch (Exception ex)
          {
            await RemoveTXOutputIndexAsync(HashCoinbaseTX);
            await RemoveTXOutputIndexesAsync(stopTX: null);
            await UnspendTXOutputsReferencedUntilTX(stopTX: TXs[i]);
            throw ex;
          }
        }
      }
      async Task<byte[]> GetUTXOKeyFreeAsync(UInt256 tXHash)
      {
        byte[] tXHashBytes = tXHash.GetBytes();
        int numberOfKeyBytes = NumberIndexKeyBytesMin;
        byte[] uTXOKey = tXHashBytes.Take(numberOfKeyBytes).ToArray();

        while (UTXO.UTXOTable.TryGetValue(uTXOKey, out byte[] UTXOIndex))
        {
          if (numberOfKeyBytes == tXHashBytes.Length)
          {
            throw new UTXOException(
              string.Format("Ambiguous transaction '{0}' in block '{1}'", tXHash, HeaderHash));
          }

          byte[] headerIndex = new ArraySegment<byte>(UTXOIndex, 0, NumberHeaderIndexBytes).Array;
          if (await UTXO.ReadTXAsync(tXHash, headerIndex) != null)
          {
            throw new UTXOException(
              string.Format("Ambiguous transaction '{0}' in block '{1}' and block header index '{2}'",
              tXHash,
              HeaderHash,
              new SoapHexBinary(headerIndex)));
          }

          numberOfKeyBytes++;
          uTXOKey = tXHashBytes.Take(numberOfKeyBytes).ToArray();
        }

        return uTXOKey;
      }
      async Task RemoveTXOutputIndexesAsync(TX stopTX)
      {
        int i = 0;
        while(TXs[i] != stopTX)
        {
          await RemoveTXOutputIndexAsync(HashesTX[i]);
          i++;
        }
      }
      async Task RemoveTXOutputIndexAsync(UInt256 tXHash)
      {
        byte[] tXHashBytes = tXHash.GetBytes();
        int numberOfKeyBytes = NumberIndexKeyBytesMin;
        byte[] uTXOKey = tXHashBytes.Take(numberOfKeyBytes).ToArray();

        while (UTXO.UTXOTable.TryGetValue(uTXOKey, out byte[] UTXOIndex))
        {
          byte[] headerIndex = new ArraySegment<byte>(UTXOIndex, 0, NumberHeaderIndexBytes).Array;
          if (await UTXO.ReadTXAsync(tXHash, headerIndex) != null)
          {
            UTXO.UTXOTable.Remove(uTXOKey);
            return;
          }

          numberOfKeyBytes++;
          uTXOKey = tXHashBytes.Take(numberOfKeyBytes).ToArray();
        }
      }
      async Task UnspendTXOutputsReferencedUntilTX(TX stopTX)
      {
        int i = 0;
        while (TXs[i] != stopTX)
        {
          await UnspendTXOutputsReferencedAsync(TXs[i].TXInputs);
          i++;
        }
      }
      async Task UnspendTXOutputsReferencedAsync(List<TXInput> tXInputs)
      {
        foreach(TXInput tXInput in tXInputs)
        {
          int byteIndex = tXInput.IndexOutput / 8;
          int bitIndex = tXInput.IndexOutput % 8;

          try
          {
            (byte[] uTXOKey, byte[] uTXOIndex, TXOutput tXOutput)
              tXOutputTuple = await GetTXOutputTupleAsync(tXInput);

            byte[] bitMapTXOutputsSpent = GetBitMapFromUTXOIndex(tXOutputTuple.uTXOIndex);
            
            try
            {
              bitMapTXOutputsSpent[byteIndex] &= (byte)~(0x01 << bitIndex);
            }
            catch(IndexOutOfRangeException)
            {
              UTXO.UTXOTable.Remove(tXOutputTuple.uTXOKey);

              byte[] uTXOIndexNEW = CreateUTXOIndex(tXInput.IndexOutput);
              bitMapTXOutputsSpent.CopyTo(uTXOIndexNEW, NumberHeaderIndexBytes);
              
              for(int i = bitMapTXOutputsSpent.Length; i < uTXOIndexNEW.Length - 1; i++)
              {
                uTXOIndexNEW[i] = 0xFF;
              }

              uTXOIndexNEW[byteIndex] = (byte)~(0x01 << bitIndex);

              UTXO.UTXOTable.Add(tXOutputTuple.uTXOKey, uTXOIndexNEW);
            }
          }
          catch (UTXOException)
          {
            byte[] uTXOIndexNEW = CreateUTXOIndex(tXInput.IndexOutput);

            for (int i = NumberHeaderIndexBytes; i < uTXOIndexNEW.Length - 1; i++)
            {
              uTXOIndexNEW[i] = 0xFF;
            }

            uTXOIndexNEW[byteIndex] = (byte)~(0x01 << bitIndex);

            byte[] uTXOKey = await GetUTXOKeyFreeAsync(tXInput.TXIDOutput);
            UTXO.UTXOTable.Add(uTXOKey, uTXOIndexNEW);
          }

        }
      }
      byte[] CreateUTXOIndex(int tXOutputsCount)
      {
        byte[] uTXOIndex = new byte[NumberHeaderIndexBytes + (tXOutputsCount + 7) / 8];
        Array.Copy(HeaderHash.GetBytes(), 0, uTXOIndex, 0, NumberHeaderIndexBytes);

        return uTXOIndex;
      }
      byte[] GetBitMapFromUTXOIndex(byte[] uTXOIndex)
      {
        return new ArraySegment<byte>(
            uTXOIndex,
            NumberHeaderIndexBytes,
            uTXOIndex.Length - NumberHeaderIndexBytes).Array;
      }
      async Task SpendTXOutputsReferencedAsync(TX tX, UInt256 tXHash)
      {
        for (int index = 0; index < tX.TXInputs.Count; index++)
        {
          TXInput tXInput = tX.TXInputs[index];

          try
          {
            await SpendTXOutputAsync(tXInput);
          }
          catch (UTXOException ex)
          {
            throw new UTXOException(
              string.Format("Spending output referenced in tXInput '{0}' in transaction '{1}' in block '{2}' threw exception.",
              index, tXHash, HeaderHash),
              ex);
          }
        }
      }
      async Task<(byte[] uTXOKey, byte[] uTXOIndex, TXOutput tXOutput)>
        GetTXOutputTupleAsync(TXInput tXInput)
      {
        byte[] tXHashBytes = tXInput.TXIDOutput.GetBytes();
        int numberOfKeyBytes = NumberIndexKeyBytesMin;
        byte[] uTXOKey = tXHashBytes.Take(numberOfKeyBytes).ToArray();

        while (UTXO.UTXOTable.TryGetValue(uTXOKey, out byte[] uTXOIndex))
        {
          byte[] headerIndex = new ArraySegment<byte>(uTXOIndex, 0, NumberHeaderIndexBytes).Array;
          TX tX = await UTXO.ReadTXAsync(tXInput.TXIDOutput, headerIndex);
          if (tX == null)
          {
            numberOfKeyBytes++;
            uTXOKey = tXHashBytes.Take(numberOfKeyBytes).ToArray();
            continue;
          }
          else
          {
            return (uTXOKey, uTXOIndex, tX.TXOutputs[tXInput.IndexOutput]);
          }
        }

        throw new UTXOException(string.Format("TXInput references nonexistant TX: '{0}'", tXInput.TXIDOutput));
      }
      async Task SpendTXOutputAsync(TXInput tXInput)
      {
        var tXOutputTuple = await GetTXOutputTupleAsync(tXInput);

        byte[] bitMapTXOutputsSpent = GetBitMapFromUTXOIndex(tXOutputTuple.uTXOIndex);
        if (IsOutputSpent(bitMapTXOutputsSpent, tXInput.IndexOutput))
        {
          throw new UTXOException(string.Format("TXOutput '{0}', index: '{1}', block index: '{2}' already spent.",
            tXInput.TXIDOutput,
            tXInput.IndexOutput,
            new SoapHexBinary(tXOutputTuple.uTXOKey)));
        }
        else
        {
          if (Script.Evaluate(tXOutputTuple.tXOutput.LockingScript, tXInput.UnlockingScript))
          {
            int byteIndex = tXInput.IndexOutput / 8;
            int bitIndex = tXInput.IndexOutput % 8;
            bitMapTXOutputsSpent[byteIndex] |= (byte)(0x01 << bitIndex);

            if (AreAllOutputsSpent(bitMapTXOutputsSpent))
            {
              UTXO.UTXOTable.Remove(tXOutputTuple.uTXOKey);
              FillGapInUTXO(tXOutputTuple.uTXOKey);
            }
          }
          else
          {
            throw new UTXOException(string.Format("Input script '{0}' failed to unlock output script '{1}'",
              new SoapHexBinary(tXInput.UnlockingScript),
              new SoapHexBinary(tXOutputTuple.tXOutput.LockingScript)));
          }
        }

      }
      bool AreAllOutputsSpent(byte[] bitMapTXOutputsSpent)
      {
        for (int i = 0; i < bitMapTXOutputsSpent.Length; i++)
        {
          if (bitMapTXOutputsSpent[i] != 0xFF) { return false; }
        }

        return true;
      }
      void FillGapInUTXO(byte[] uTXOKeyGap)
      {
        byte[] uTXOKeyNext = new byte[uTXOKeyGap.Length + 1];
        Array.Copy(uTXOKeyGap, uTXOKeyNext, uTXOKeyGap.Length);

        for (int i = 0; i < 256; i++)
        {
          uTXOKeyNext[uTXOKeyGap.Length] = (byte)i;
          if (UTXO.UTXOTable.TryGetValue(uTXOKeyNext, out byte[] uTXOIndexNext))
          {
            UTXO.UTXOTable.Add(uTXOKeyGap, uTXOIndexNext);
            UTXO.UTXOTable.Remove(uTXOKeyNext);
            FillGapInUTXO(uTXOKeyNext);
            break;
          }
        }
      }

      public async Task BuildAsync()
      {
        byte[] uTXOKey = await GetUTXOKeyFreeAsync(HashCoinbaseTX);
        byte[] uTXOIndex = CreateUTXOIndex(CoinbaseTX.TXOutputs.Count);
        UTXO.UTXOTable.Add(uTXOKey, uTXOIndex);

        for (int i = 0; i < TXs.Count; i++)
        {
          HashesTX[i] = TXs[i].GetTXHash();

          try
          {
            uTXOKey = await GetUTXOKeyFreeAsync(HashesTX[i]);
          }
          catch (UTXOException ex)
          {
            await RemoveTXOutputIndexAsync(HashCoinbaseTX);
            await RemoveTXOutputIndexesAsync(stopTX: TXs[i]);
            throw ex;
          }

          uTXOIndex = CreateUTXOIndex(TXs[i].TXOutputs.Count);
          UTXO.UTXOTable.Add(uTXOKey, uTXOIndex);
        }
        for (int i = 0; i < TXs.Count; i++)
        {
          try
          {
            await SpendTXOutputsReferencedAsync(TXs[i], HashesTX[i]);
          }
          catch (Exception ex)
          {
            await RemoveTXOutputIndexAsync(HashCoinbaseTX);
            await RemoveTXOutputIndexesAsync(stopTX: null);
            await UnspendTXOutputsReferencedUntilTX(stopTX: TXs[i]);
            throw ex;
          }
        }
      }
    }
  }
}
