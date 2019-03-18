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

      public async Task InsertAsync()
      {
        for (int i = 0; i < TXs.Count; i++)
        {
          try
          {
            byte[] uTXO = CreateUTXO(HeaderHash, TXs[i].Outputs.Count);
            //UTXO.Write(TXHashes[i], uTXO);

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
            //await SpendUTXOsAsync(TXs[i], TXHashes[i], uTXOs);
          }
          catch (Exception ex)
          {
            await RemoveTXOutputIndexesAsync(stopTX: null);
            await UnspendTXOutputsReferencedUntilTX(stopTX: TXs[i]);
            throw ex;
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

        while (UTXO.SecondaryCache.TryGetValue(uTXOKey, out byte[] UTXOIndex))
        {
          byte[] headerIndex = new ArraySegment<byte>(UTXOIndex, 0, NumberHeaderIndexBytes).Array;
          if (await UTXO.ReadTXAsync(tXHash, headerIndex) != null)
          {
            UTXO.SecondaryCache.Remove(uTXOKey);
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
              UTXO.SecondaryCache.Remove(tXOutputTuple.uTXOKey);

              byte[] uTXOIndexNEW = CreateUTXO(HeaderHash, tXInput.IndexOutput);
              bitMapTXOutputsSpent.CopyTo(uTXOIndexNEW, NumberHeaderIndexBytes);
              
              for(int i = bitMapTXOutputsSpent.Length; i < uTXOIndexNEW.Length - 1; i++)
              {
                uTXOIndexNEW[i] = 0xFF;
              }

              uTXOIndexNEW[byteIndex] = (byte)~(0x01 << bitIndex);

              UTXO.SecondaryCache.Add(tXOutputTuple.uTXOKey, uTXOIndexNEW);
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
            UTXO.SecondaryCache.Add(uTXOKey, uTXOIndexNEW);
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
      async Task<(byte[] uTXOKey, byte[] uTXOIndex, TXOutput tXOutput)>
        GetTXOutputTupleAsync(TXInput tXInput)
      {
        byte[] tXHashBytes = tXInput.TXIDOutput;
        int numberOfKeyBytes = NumberIndexKeyBytesMin;
        byte[] uTXOKey = tXHashBytes.Take(numberOfKeyBytes).ToArray();

        while (UTXO.SecondaryCache.TryGetValue(uTXOKey, out byte[] uTXOIndex))
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


      async Task<byte[]> GetUTXOKeyFreeAsync(byte[] tXHash)
      {
        int numberOfKeyBytes = NumberIndexKeyBytesMin;
        byte[] uTXOKey = tXHash.Take(numberOfKeyBytes).ToArray();

        while (UTXO.SecondaryCache.TryGetValue(uTXOKey, out byte[] UTXOIndex))
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
    }
  }
}
