using System.Diagnostics;

using System;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using System.Threading.Tasks;

namespace BToken.Accounting
{
  public partial class UTXO
  {
    class UTXOTableByteArray : UTXOTable
    {
      Dictionary<int, byte[]> PrimaryCache = new Dictionary<int, byte[]>();
      Dictionary<byte[], byte[]> SecondaryCache =
        new Dictionary<byte[], byte[]>(new EqualityComparerByteArray());
      
      byte[] UTXOPrimaryExisting;
      byte[] UTXOSecondaryExisting;

      byte[] MasksCollision = {
        0x04,
        0x08,
        0x10 };

      const int LENGTH_HEADER_INDEX_BYTES = (COUNT_BATCHINDEX_BITS + 7) / 8;
      const int COUNT_NON_HEADER_BITS_IN_BYTE = (8 - COUNT_BATCHINDEX_BITS % 8) % 8;
      static readonly int CountNonOutputsBitsInByte = CountNonOutputBits % 8;
      static readonly byte MaskAllOutputsBitsInByte = (byte)(byte.MaxValue << CountNonOutputsBitsInByte);
      static readonly int OutputBitsByteIndex = CountNonOutputBits / 8;
      static readonly int ByteIndexCollisionBits = COUNT_BATCHINDEX_BITS / 8;
      static readonly int CountHeaderBitsInByte = COUNT_BATCHINDEX_BITS % 8;

      static readonly int CountBatchIndexBytes = (COUNT_BATCHINDEX_BITS + 7) / 8;
      static readonly int ByteIndexHeaderBits = COUNT_BATCHINDEX_BITS / 8;
      static readonly byte MaskHeaderBits = byte.MaxValue >> (8 - COUNT_HEADER_BITS);

      public UTXOTableByteArray() : base(2, "ByteArray")
      { }

      protected override int GetCountPrimaryCacheItems()
      {
        return PrimaryCache.Count;
      }
      protected override int GetCountSecondaryCacheItems()
      {
        return SecondaryCache.Count;
      }

      public override bool TrySetCollisionBit(int primaryKey, int collisionAddress)
      {
        if (PrimaryCache.TryGetValue(primaryKey, out byte[] uTXOExisting))
        {
          uTXOExisting[ByteIndexCollisionBits] |= MasksCollision[collisionAddress];
          return true;
        }

        return false;
      }
      public override void SecondaryCacheAddUTXO(UTXOItem uTXODataItem)
      {
        SecondaryCache.Add(
          uTXODataItem.Hash,
          ((UTXOItemByteArray)uTXODataItem).UTXOIndex);
      }
      public override void PrimaryCacheAddUTXO(UTXOItem uTXODataItem)
      {
        PrimaryCache.Add(
          uTXODataItem.PrimaryKey,
          ((UTXOItemByteArray)uTXODataItem).UTXOIndex);
      }

      public override void SpendPrimaryUTXO(TXInput input, out bool areAllOutputpsSpent)
      {
        SpendUTXO(UTXOPrimaryExisting, input.OutputIndex, out areAllOutputpsSpent);
      }
      public override bool TryGetValueInPrimaryCache(int primaryKey)
      {
        return PrimaryCache.TryGetValue(primaryKey, out UTXOPrimaryExisting);
      }
      public override bool IsCollision(int cacheAddress)
      {
        return 
          (MasksCollision[cacheAddress] & 
          UTXOPrimaryExisting[ByteIndexCollisionBits]) 
          != 0;
      }
      public override void RemovePrimary(int primaryKey)
      {
        PrimaryCache.Remove(primaryKey);
      }
      public override void ResolveCollision(int primaryKey, uint collisionBits)
      {
        KeyValuePair<byte[], byte[]> secondaryCacheItem =
          SecondaryCache.First(k => BitConverter.ToInt32(k.Key, 0) == primaryKey);
        SecondaryCache.Remove(secondaryCacheItem.Key);

        if (!SecondaryCache.Keys.Any(key => BitConverter.ToInt32(key, 0) == primaryKey))
        {
          collisionBits &= ~((uint)1 << Address);
        }

        byte[] uTXO = secondaryCacheItem.Value;
        uTXO[ByteIndexCollisionBits] |= (byte)(collisionBits << CountHeaderBitsInByte);

        PrimaryCache.Add(primaryKey, uTXO);
      }

      protected override void SpendSecondaryUTXO(byte[] key, int outputIndex, out bool areAllOutputpsSpent)
      {
        SpendUTXO(UTXOSecondaryExisting, outputIndex, out areAllOutputpsSpent);
        SecondaryCache[key] = UTXOSecondaryExisting;
      }
      protected override bool TryGetValueInSecondaryCache(byte[] key)
      {
        return SecondaryCache.TryGetValue(key, out UTXOSecondaryExisting);
      }
      protected override void RemoveSecondary(int primaryKey, byte[] key, out bool hasMoreCollisions)
      {
        SecondaryCache.Remove(key);

        hasMoreCollisions = SecondaryCache.Keys
          .Any(k => BitConverter.ToInt32(k, 0) == primaryKey);
      }
      protected override void ClearCollisionBit(int primaryKey, int cacheAddress)
      {
        UTXOPrimaryExisting[ByteIndexCollisionBits] &= (byte)~MasksCollision[cacheAddress];
      }

      static void SpendUTXO(byte[] uTXO, int outputIndex, out bool areAllOutputpsSpent)
      {
        int bitOffset = CountNonOutputBits + outputIndex;
        int byteIndex = bitOffset / 8;
        int bitIndex = bitOffset % 8;

        byte mask = (byte)(1 << bitIndex);
        if ((uTXO[byteIndex] & mask) != 0x00)
        {
          throw new UTXOException(string.Format(
            "Output index {0} already spent.", outputIndex));
        }
        uTXO[byteIndex] |= mask;

        areAllOutputpsSpent = AreAllOutputBitsSpent(uTXO);
      }
      static bool AreAllOutputBitsSpent(byte[] uTXO)
      {
        if ((uTXO[OutputBitsByteIndex] & MaskAllOutputsBitsInByte) != MaskAllOutputsBitsInByte)
        {
          return false;
        }

        int byteIndex = OutputBitsByteIndex + 1;
        while (byteIndex < uTXO.Length)
        {
          if (uTXO[byteIndex++] != 0xFF)
          {
            return false;
          }
        }

        return true;
      }

      protected override byte[] GetPrimaryData()
      {
        var byteList = new List<byte>();

        foreach (KeyValuePair<int, byte[]> keyValuePair in PrimaryCache)
        {
          byteList.AddRange(BitConverter.GetBytes(keyValuePair.Key));
          byteList.AddRange(VarInt.GetBytes(keyValuePair.Value.Length));
          byteList.AddRange(keyValuePair.Value);
        }

        return byteList.ToArray();
      }
      protected override byte[] GetSecondaryData()
      {
        var byteList = new List<byte>();

        foreach (KeyValuePair<byte[], byte[]> keyValuePair in SecondaryCache)
        {
          byteList.AddRange(keyValuePair.Key);
          byteList.AddRange(VarInt.GetBytes(keyValuePair.Value.Length));
          byteList.AddRange(keyValuePair.Value);
        }

        return byteList.ToArray();
      }

      protected override void LoadPrimaryData(byte[] buffer)
      {
        int index = 0;

        int key;
        int lengthValue;
        byte[] value;

        try
        {
          while (index < buffer.Length)
          {
            key = BitConverter.ToInt32(buffer, index);
            index += 4;

            lengthValue = VarInt.GetInt32(buffer, ref index);
            value = new byte[lengthValue];
            Array.Copy(buffer, index, value, 0, lengthValue);
            index += lengthValue;

            PrimaryCache.Add(key, value);
          }
        }
        catch(Exception ex)
        {
          Console.WriteLine(ex.Message);
        }
      }
      protected override void LoadSecondaryData(byte[] buffer)
      {
        int index = 0;

        while (index < buffer.Length)
        {
          byte[] key = new byte[HASH_BYTE_SIZE];
          Array.Copy(buffer, index, key, 0, HASH_BYTE_SIZE);
          index += HASH_BYTE_SIZE;

          int lengthValue = VarInt.GetInt32(buffer, ref index);
          byte[] value = new byte[lengthValue];
          Array.Copy(buffer, index, value, 0, lengthValue);
          index += lengthValue;

          SecondaryCache.Add(key, value);
        }
      }
      
      public override void Clear()
      {
        PrimaryCache.Clear();
        SecondaryCache.Clear();
      }

      public override bool TryParseUTXO(
        int batchIndex,
        byte[] headerHash,
        int lengthUTXOBits,
        out UTXOItem item)
      {
        byte[] uTXOIndex = new byte[(lengthUTXOBits + 7) / 8];

        int countUTXORemainderBits = lengthUTXOBits % 8;
        if (countUTXORemainderBits > 0)
        {
          uTXOIndex[uTXOIndex.Length - 1] |= (byte)(byte.MaxValue << countUTXORemainderBits);
        }

        for (int i = 0; i < CountBatchIndexBytes; i += 1)
        {
          uTXOIndex[i] = (byte)(batchIndex >> i);
        }

        uTXOIndex[ByteIndexHeaderBits] = (byte)(headerHash[0] & MaskHeaderBits);

        item = new UTXOItemByteArray
        {
          UTXOIndex = uTXOIndex
        };

        return true;
      }
    }

    class UTXOItemByteArray : UTXOItem
    {
      public byte[] UTXOIndex;
    }
  }
}
