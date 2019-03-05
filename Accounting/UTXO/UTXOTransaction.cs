using System;
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
      List<byte[]> TXHashes;

      const int NumberIndexKeyBytesMin = 4;
      const int NumberHeaderIndexBytes = 4;


      public UTXOTransaction(UTXO uTXO, Block block)
      {
        UTXO = uTXO;
        HeaderHash = block.HeaderHash;
        TXs = block.TXs;
        TXHashes = block.TXHashes;
      }

      public async Task InsertAsync(Dictionary<byte[], byte[]> uTXOs)
      {
        for (int i = 0; i < TXs.Count; i++)
        {
          try
          {
            byte[] uTXO = CreateUTXO(HeaderHash, TXs[i].Outputs.Count);
            uTXOs.Add(TXHashes[i], uTXO);

            //await UTXOArchiver.ArchiveUTXOAsync(TXHashes[i], uTXO);
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
            await SpendUTXOsAsync(TXs[i], TXHashes[i], uTXOs);
          }
          catch (Exception ex)
          {
            await RemoveTXOutputIndexesAsync(stopTX: null);
            await UnspendTXOutputsReferencedUntilTX(stopTX: TXs[i]);
            throw ex;
          }
        }
      }
      async Task SpendUTXOsAsync(TX tX, byte[] tXHash, Dictionary<byte[], byte[]> uTXOs)
      {
        foreach (TXInput tXInput in tX.Inputs)
        {
          if(uTXOs.TryGetValue(tXInput.TXIDOutput, out byte[] uTXO))
          {
            SpendOutputsBits(uTXO, new List<int> { tXInput.IndexOutput });

            if (AreAllOutputBitsSpent(uTXO))
            {
              uTXOs.Remove(tXInput.TXIDOutput);

              await UTXOArchiver.DeleteUTXOAsync(tXInput.TXIDOutput);
            }
          }
          else
          {
            throw new UTXOException(string.Format("TXInput references nonexistant TX: '{0}'", tXInput.TXIDOutput));
          }
        }
      }
      
      async Task RemoveTXOutputIndexesAsync(TX stopTX)
      {
        int i = 0;
        while(TXs[i] != stopTX)
        {
          await RemoveTXOutputIndexAsync(TXHashes[i]);
          i++;
        }
      }
      async Task RemoveTXOutputIndexAsync(byte[] tXHash)
      {
        int numberOfKeyBytes = NumberIndexKeyBytesMin;
        byte[] uTXOKey = tXHash.Take(numberOfKeyBytes).ToArray();

        while (UTXO.UTXOs.TryGetValue(uTXOKey, out byte[] UTXOIndex))
        {
          byte[] headerIndex = new ArraySegment<byte>(UTXOIndex, 0, NumberHeaderIndexBytes).Array;
          if (await UTXO.ReadTXAsync(tXHash, headerIndex) != null)
          {
            UTXO.UTXOs.Remove(uTXOKey);
            return;
          }

          numberOfKeyBytes++;
          uTXOKey = tXHash.Take(numberOfKeyBytes).ToArray();
        }
      }
      async Task UnspendTXOutputsReferencedUntilTX(TX stopTX)
      {
        int i = 0;
        while (TXs[i] != stopTX)
        {
          await UnspendTXOutputsReferencedAsync(TXs[i].Inputs);
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
              UTXO.UTXOs.Remove(tXOutputTuple.uTXOKey);

              byte[] uTXOIndexNEW = CreateUTXO(HeaderHash, tXInput.IndexOutput);
              bitMapTXOutputsSpent.CopyTo(uTXOIndexNEW, NumberHeaderIndexBytes);
              
              for(int i = bitMapTXOutputsSpent.Length; i < uTXOIndexNEW.Length - 1; i++)
              {
                uTXOIndexNEW[i] = 0xFF;
              }

              uTXOIndexNEW[byteIndex] = (byte)~(0x01 << bitIndex);

              UTXO.UTXOs.Add(tXOutputTuple.uTXOKey, uTXOIndexNEW);
            }
          }
          catch (UTXOException)
          {
            byte[] uTXOIndexNEW = CreateUTXO(HeaderHash, tXInput.IndexOutput);

            for (int i = NumberHeaderIndexBytes; i < uTXOIndexNEW.Length - 1; i++)
            {
              uTXOIndexNEW[i] = 0xFF;
            }

            uTXOIndexNEW[byteIndex] = (byte)~(0x01 << bitIndex);

            byte[] uTXOKey = await GetUTXOKeyFreeAsync(tXInput.TXIDOutput);
            UTXO.UTXOs.Add(uTXOKey, uTXOIndexNEW);
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
      async Task SpendTXOutputsReferencedAsync(TX tX, byte[] tXHash)
      {
        for (int index = 0; index < tX.Inputs.Count; index++)
        {
          TXInput tXInput = tX.Inputs[index];

          try
          {
            await SpendTXOutputAsync(tXInput);
          }
          catch (UTXOException ex)
          {
            throw new UTXOException(
              string.Format("Spending output referenced in tXInput '{0}' in transaction '{1}' in block '{2}' threw exception.",
              index, new SoapHexBinary(tXHash), HeaderHash),
              ex);
          }
        }
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
            SpendOutputsBits(tXOutputTuple.uTXOIndex, new List<int> { tXInput.IndexOutput });

            if (AreAllOutputBitsSpent(tXOutputTuple.uTXOIndex))
            {
              UTXO.UTXOs.Remove(tXOutputTuple.uTXOKey);
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
      async Task<(byte[] uTXOKey, byte[] uTXOIndex, TXOutput tXOutput)>
        GetTXOutputTupleAsync(TXInput tXInput)
      {
        byte[] tXHashBytes = tXInput.TXIDOutput;
        int numberOfKeyBytes = NumberIndexKeyBytesMin;
        byte[] uTXOKey = tXHashBytes.Take(numberOfKeyBytes).ToArray();

        while (UTXO.UTXOs.TryGetValue(uTXOKey, out byte[] uTXOIndex))
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
            return (uTXOKey, uTXOIndex, tX.Outputs[tXInput.IndexOutput]);
          }
        }

        throw new UTXOException(string.Format("TXInput references nonexistant TX: '{0}'", tXInput.TXIDOutput));
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

      void FillGapInUTXO(byte[] uTXOKeyGap)
      {
        byte[] uTXOKeyNext = new byte[uTXOKeyGap.Length + 1];
        Array.Copy(uTXOKeyGap, uTXOKeyNext, uTXOKeyGap.Length);

        for (int i = 0; i < 256; i++)
        {
          uTXOKeyNext[uTXOKeyGap.Length] = (byte)i;
          if (UTXO.UTXOs.TryGetValue(uTXOKeyNext, out byte[] uTXOIndexNext))
          {
            UTXO.UTXOs.Add(uTXOKeyGap, uTXOIndexNext);
            UTXO.UTXOs.Remove(uTXOKeyNext);
            FillGapInUTXO(uTXOKeyNext);
            break;
          }
        }
      }
      
      static byte[] CreateUTXO(UInt256 headerHash, int outputsCount)
      {
        byte[] uTXOIndex = new byte[CountHeaderIndexBytes + (outputsCount + 7) / 8];

        int numberOfRemainderBits = outputsCount % 8;
        if(numberOfRemainderBits > 0)
        {
          SpendExcessBits(uTXOIndex, numberOfRemainderBits);
        }
        
        Array.Copy(headerHash.GetBytes(), uTXOIndex, CountHeaderIndexBytes);

        return uTXOIndex;
      }
      static void SpendExcessBits(byte[] uTXOIndex, int numberOfRemainderBits)
      {
        for (int i = numberOfRemainderBits; i < 8; i++)
        {
          uTXOIndex[uTXOIndex.Length - 1] |= (byte)(0x01 << i);
        }
      }
      async Task InsertUTXOAsync(byte[] uTXOIndex, byte[] hashTX)
      {
        byte[] uTXOKey = await GetUTXOKeyFreeAsync(hashTX);
        UTXO.UTXOs.Add(uTXOKey, uTXOIndex);
      }
      async Task<byte[]> GetUTXOKeyFreeAsync(byte[] tXHash)
      {
        int numberOfKeyBytes = NumberIndexKeyBytesMin;
        byte[] uTXOKey = tXHash.Take(numberOfKeyBytes).ToArray();

        while (UTXO.UTXOs.TryGetValue(uTXOKey, out byte[] UTXOIndex))
        {
          if (numberOfKeyBytes == tXHash.Length)
          {
            throw new UTXOException(
              string.Format("Ambiguous transaction '{0}' in block '{1}'", 
              new SoapHexBinary(tXHash), 
              HeaderHash));
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
          uTXOKey = tXHash.Take(numberOfKeyBytes).ToArray();
        }

        return uTXOKey;
      }
      

      public static void BuildBlock(
        Block block, 
        Dictionary<byte[], List<int>> inputsUnfunded,
        Dictionary<byte[], byte[]> uTXOs)
      {
        List<TX> tXs = block.TXs;
        List<byte[]> tXHashes = block.TXHashes;
        UInt256 headerHash = block.HeaderHash;

        for (int t = 1; t < tXs.Count; t++)
        {
          for (int i = 0; i < tXs[t].Inputs.Count; i++)
          {
            InsertInput(tXs[t].Inputs[i], inputsUnfunded);
          }
        }

        for (int t = 0; t < tXs.Count; t++)
        {
          InsertTXOutputs(
            headerHash, 
            tXs[t], 
            tXHashes[t],
            inputsUnfunded,
            uTXOs);
        }
      }

      static void InsertInput(TXInput input, Dictionary<byte[], List<int>> inputs)
      {
        if (inputs.TryGetValue(input.TXIDOutput, out List<int> inputsExisting))
        {
          if (inputsExisting.Any(i => i == input.IndexOutput))
          {
            throw new UTXOException(string.Format("Double spent output. TX = '{0}', index = '{1}'.",
              Bytes2HexStringReversed(input.TXIDOutput),
              input.IndexOutput));
          }
          else
          {
            inputsExisting.Add(input.IndexOutput);
          }
        }
        else
        {
          inputs.Add(input.TXIDOutput, new List<int> { input.IndexOutput });
        }
      }

      static void InsertTXOutputs(
        UInt256 headerHash, 
        TX tX, 
        byte[] tXHash, 
        Dictionary<byte[], List<int>> inputsUnfunded,
        Dictionary<byte[], byte[]> uTXOs)
      {
        byte[] uTXO = CreateUTXO(headerHash, tX.Outputs.Count);
        
        if (inputsUnfunded.TryGetValue(tXHash, out List<int> outputIndexes))
        {
          try
          {
            SpendOutputsBits(uTXO, outputIndexes);
          }
          catch(Exception ex)
          {
            Console.WriteLine("Spend '{0}' inputsUnfunded on tXOutputs threw exception '{1}'.",
              outputIndexes.Count,
              ex.Message);
          }

          inputsUnfunded.Remove(tXHash);
        }

        if (!AreAllOutputBitsSpent(uTXO))
        {
          try
          {
            uTXOs.Add(tXHash.Take(6).ToArray(), uTXO);
          }
          catch (ArgumentException)
          {
            Console.WriteLine("Ambiguous transaction '{0}' in block '{1}'",
              new SoapHexBinary(tXHash), headerHash);
          }
        }
      }
    }
  }
}
