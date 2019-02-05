using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.Remoting.Metadata.W3cXsd2001;
using System.Threading.Tasks;

namespace BToken.Accounting
{
  partial class UTXO
  {
    class UTXOTransaction
    {
      UTXO UTXO;
      UInt256 HeaderHash;
      List<TX> TXs;
      UInt256[] HashesTX;

      int NumberIndexKeyBytesMin = 4;
      int NumberHeaderIndexBytes = 4;


      public UTXOTransaction(UTXO uTXO, List<TX> tXs, UInt256 blockHeaderHash)
      {
        UTXO = uTXO;
        HeaderHash = blockHeaderHash;
        TXs = tXs;
        HashesTX = new UInt256[TXs.Count];
      }

      public async Task InsertAsync()
      {

        for (int i = 0; i < TXs.Count; i++)
        {
          HashesTX[i] = TXs[i].GetTXHash();

          try
          {
            await InsertNewTXOutputsAsync(TXs[i].TXOutputs.Count, HashesTX[i]);
          }
          catch (UTXOException ex)
          {
            await RemoveTXOutputIndexesAsync(stopTX: TXs[i]);
            throw ex;
          }

        }

        for (int i = 0; i < TXs.Count; i++)
        {
          try
          {
            await SpendTXOutputsReferencedAsync(TXs[i], HashesTX[i]);
          }
          catch (Exception ex)
          {
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

        if (IsOutputSpent(tXOutputTuple.uTXOIndex, tXInput.IndexOutput))
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
            SpendTXOutputBit(tXOutputTuple.uTXOIndex, tXInput.IndexOutput);

            if (AreAllOutputBitsSpent(tXOutputTuple.uTXOIndex))
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
      bool IsOutputSpent(byte[] uTXOIndex, int index)
      {
        int byteIndex = index / 8 + NumberHeaderIndexBytes;
        int bitIndex = index % 8;
        byte maskFlag = (byte)(0x01 << bitIndex);

        try
        {
          return (maskFlag & uTXOIndex[byteIndex]) != 0x00;
        }
        catch (ArgumentOutOfRangeException)
        {
          return true;
        }
      }
      bool AreAllOutputBitsSpent(byte[] uTXOIndex)
      {
        for (int i = NumberHeaderIndexBytes; i < uTXOIndex.Length; i++)
        {
          if (uTXOIndex[i] != 0xFF) { return false; }
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

      public async Task BuildAsync(Dictionary<UInt256, List<TXInput>> tXInputsUnfunded)
      {
        InsertInputsUnfunded(tXInputsUnfunded);
                        
        for (int i = 0; i < TXs.Count; i++)
        {
          HashesTX[i] = TXs[i].GetTXHash();
          await BuildTXOutputsAsync(TXs[i].TXOutputs.Count, HashesTX[i], tXInputsUnfunded);
        }
      }
      void InsertInputsUnfunded(Dictionary<UInt256, List<TXInput>> txInputsUnfundedTotal)
      {
        for (int t = 1; t < TXs.Count; t++)
        {
          for (int i = 0; i < TXs[t].TXInputs.Count; i++)
          {
            TXInput tXInput = TXs[t].TXInputs[i];
            if (txInputsUnfundedTotal.TryGetValue(tXInput.TXIDOutput, out List<TXInput> tXInputsUnfunded))
            {
              if (tXInputsUnfunded.Any(tu => tu.IndexOutput == tXInput.IndexOutput))
              {
                throw new UTXOException("Double spend detected during UTXO build.");
              }
              else
              {
                tXInputsUnfunded.Add(tXInput);
              }
            }
            else
            {
              tXInputsUnfunded = new List<TXInput> { tXInput };
              txInputsUnfundedTotal.Add(tXInput.TXIDOutput, tXInputsUnfunded);
            }
          }
        }
      }
      async Task BuildTXOutputsAsync(int countTXOutputs, UInt256 hashTX, Dictionary<UInt256, List<TXInput>> tXInputsUnfunded)
      {
        if (tXInputsUnfunded.TryGetValue(hashTX, out List<TXInput> tXInputs))
        {
          byte[] uTXOIndex = CreateUTXOIndex(countTXOutputs);

          for (int i = 0; i < tXInputs.Count; i++)
          {
            TXInput tXInput = tXInputs[i];
            SpendTXOutputBit(uTXOIndex, tXInputs[i].IndexOutput);
          }

          await InsertUTXOIndexAsync(uTXOIndex, hashTX);
          tXInputsUnfunded.Remove(hashTX);
        }
        else
        {
          await InsertNewTXOutputsAsync(countTXOutputs, hashTX);
        }
      }

      byte[] CreateUTXOIndex(int tXOutputsCount)
      {
        byte[] uTXOIndex = new byte[NumberHeaderIndexBytes + (tXOutputsCount + 7) / 8];

        int numberOfBitsRemainder = tXOutputsCount % 8;
        if (numberOfBitsRemainder > 0)
        {
          SpendExcessBits(uTXOIndex, numberOfBitsRemainder);
        }

        Array.Copy(HeaderHash.GetBytes(), 0, uTXOIndex, 0, NumberHeaderIndexBytes);

        return uTXOIndex;
      }
      void SpendExcessBits(byte[] uTXOIndex, int numberOfBitsRemainder)
      {
        for (int i = numberOfBitsRemainder; i < 8; i++)
        {
          uTXOIndex[uTXOIndex.Length - 1] |= (byte)(0x01 << i);
        }
      }
      async Task InsertNewTXOutputsAsync(int countTXOutputs, UInt256 hashTX)
      {
        byte[] uTXOIndex = CreateUTXOIndex(countTXOutputs);
        await InsertUTXOIndexAsync(uTXOIndex, hashTX);
      }
      async Task InsertUTXOIndexAsync(byte[] uTXOIndex, UInt256 hashTX)
      {
        byte[] uTXOKey = await GetUTXOKeyFreeAsync(hashTX);
        UTXO.UTXOTable.Add(uTXOKey, uTXOIndex);
      }
      void SpendTXOutputBit(byte[] uTXOIndex, int indexTXOutput)
      {
        int byteIndex = indexTXOutput / 8 + NumberHeaderIndexBytes;
        int bitIndex = indexTXOutput % 8;
        uTXOIndex[byteIndex] |= (byte)(0x01 << bitIndex);
      }
    }
  }
}
