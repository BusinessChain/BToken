using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace BToken.Accounting
{
  public partial class UTXO
  {
    class UTXOCacheByteArray : UTXOCache
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

      const int LENGTH_HEADER_INDEX_BYTES = (COUNT_HEADERINDEX_BITS + 7) / 8;
      const int COUNT_NON_HEADER_BITS_IN_BYTE = (8 - COUNT_HEADERINDEX_BITS % 8) % 8;
      

      public UTXOCacheByteArray()
        : this(null)
      { }
      public UTXOCacheByteArray(UTXOCache nextCache) 
        : base(1, nextCache)
      { }


      public override int GetCountPrimaryCacheItems()
      {
        return PrimaryCache.Count;
      }
      public override int GetCountSecondaryCacheItems()
      {
        return SecondaryCache.Count;
      }

      protected override bool TryInsertUTXO(
        int primaryKey,
        byte[] tXIDHash,
        byte[] headerHashBytes,
        int lengthUTXOBits)
      {
        byte[] uTXO = CreateUTXOByteArray(headerHashBytes, lengthUTXOBits);

        if (PrimaryCache.TryGetValue(primaryKey, out byte[] uTXOExisting))
        {
          uTXOExisting[ByteIndexCollisionBits] |= MasksCollision[Address];
          SecondaryCache.Add(tXIDHash, uTXO);
        }
        else
        {
          PrimaryCache.Add(primaryKey, uTXO);
        }

        return true;
      }
      static byte[] CreateUTXOByteArray(byte[] headerHashBytes, int lengthUTXOBits)
      {
        int lengthUTXOIndex = (lengthUTXOBits + 7) / 8;
        byte[] uTXOIndex = new byte[lengthUTXOIndex];

        Array.Copy(headerHashBytes, uTXOIndex, LENGTH_HEADER_INDEX_BYTES);

        int i = LENGTH_HEADER_INDEX_BYTES - 1;
        uTXOIndex[i] <<= COUNT_NON_HEADER_BITS_IN_BYTE;
        uTXOIndex[i] >>= COUNT_NON_HEADER_BITS_IN_BYTE;

        int countUTXORemainderBits = lengthUTXOBits % 8;
        if(countUTXORemainderBits > 0)
        {
          uTXOIndex[uTXOIndex.Length - 1] |= (byte)(byte.MaxValue << countUTXORemainderBits);
        }

        return uTXOIndex;
      }


      protected override void SpendPrimaryUTXO(int outputIndex, out bool areAllOutputpsSpent)
      {
        SpendUTXO(UTXOPrimaryExisting, outputIndex, out areAllOutputpsSpent);
      }
      protected override bool TryGetValueInPrimaryCache(int primaryKey)
      {
        return PrimaryCache.TryGetValue(primaryKey, out UTXOPrimaryExisting);
      }
      protected override bool IsCollision(int cacheAddress)
      {
        return 
          (MasksCollision[cacheAddress] & 
          UTXOPrimaryExisting[ByteIndexCollisionBits]) 
          != 0;
      }
      protected override void RemovePrimary(int primaryKey)
      {
        PrimaryCache.Remove(primaryKey);
      }
      protected override void ResolveCollision(int primaryKey, uint collisionBits)
      {
        KeyValuePair<byte[], byte[]> secondaryCacheItem =
          SecondaryCache.First(k => BitConverter.ToInt32(k.Key, 0) == primaryKey);
        SecondaryCache.Remove(secondaryCacheItem.Key);

        if (!SecondaryCache.Keys.Any(key => BitConverter.ToInt32(key, 0) == primaryKey))
        {
          collisionBits &= ~((uint)1 << IndexCacheByteArray);
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
      protected override void ClearCollisionBit(int cacheAddress)
      {
        UTXOPrimaryExisting[ByteIndexCollisionBits] &= (byte)~MasksCollision[cacheAddress];
      }

      static void SpendUTXO(byte[] uTXO, int outputIndex, out bool areAllOutputpsSpent)
      {
        int bitOffset = CountHeaderPlusCollisionBits + outputIndex;
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

    }
  }
}
