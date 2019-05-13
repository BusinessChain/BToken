using System;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using System.Threading.Tasks;

namespace BToken.Accounting
{
  public partial class UTXO
  {
    class UTXOCacheULong64 : UTXOCache
    {
      Dictionary<int, ulong> PrimaryCache = new Dictionary<int, ulong>();
      Dictionary<byte[], ulong> SecondaryCache =
        new Dictionary<byte[], ulong>(new EqualityComparerByteArray());

      ulong UTXOIndex;
      ulong UTXOPrimaryExisting;
      ulong UTXOSecondaryExisting;

      const int COUNT_LONG_BITS = sizeof(long) * 8;

      static readonly ulong MaskAllOutputBitsSpent = ulong.MaxValue << CountHeaderPlusCollisionBits;
      static readonly int CountNonHeaderBits = COUNT_LONG_BITS - COUNT_HEADERINDEX_BITS;

      ulong[] MasksCollision = {
        0x04000000,
        0x08000000,
        0x10000000 };


      public UTXOCacheULong64(UTXOCache nextCache)
        : base(nextCache, "ULong64")
      { }


      protected override int GetCountPrimaryCacheItems()
      {
        return PrimaryCache.Count;
      }
      protected override int GetCountSecondaryCacheItems()
      {
        return SecondaryCache.Count;
      }

      protected override bool IsUTXOTooLongForCache(int lengthUTXOBits)
      {
        return COUNT_LONG_BITS < lengthUTXOBits;
      }
      protected override void CreateUTXO(byte[] headerHashBytes, int lengthUTXOBits)
      {
        UTXOIndex = 0;

        for (int i = CountHeaderBytes; i > 0; i--)
        {
          UTXOIndex <<= 8;
          UTXOIndex |= headerHashBytes[i - 1];
        }
        UTXOIndex <<= CountNonHeaderBits;
        UTXOIndex >>= CountNonHeaderBits;

        if (COUNT_LONG_BITS > lengthUTXOBits)
        {
          UTXOIndex |= (ulong.MaxValue << lengthUTXOBits);
        }
      }
      protected override bool TrySetCollisionBit(int primaryKey, int collisionAddress)
      {
        if (PrimaryCache.ContainsKey(primaryKey))
        {
          PrimaryCache[primaryKey] |= MasksCollision[collisionAddress];
          return true;
        }

        return false;
      }
      protected override void SecondaryCacheAddUTXO(byte[] tXIDHash)
      {
        SecondaryCache.Add(tXIDHash, UTXOIndex);
      }
      protected override void PrimaryCacheAddUTXO(int primaryKey)
      {
        PrimaryCache.Add(primaryKey, UTXOIndex);
      }

      protected override void SpendPrimaryUTXO(int outputIndex, out bool areAllOutputpsSpent)
      {
        SpendUTXO(ref UTXOPrimaryExisting, outputIndex, out areAllOutputpsSpent);
        PrimaryCache[PrimaryKey] = UTXOPrimaryExisting;
      }
      protected override bool TryGetValueInPrimaryCache(int primaryKey)
      {
        return PrimaryCache.TryGetValue(primaryKey, out UTXOPrimaryExisting);
      }
      protected override bool IsCollision(int cacheAddress)
      {
        return (MasksCollision[cacheAddress] & UTXOPrimaryExisting) != 0;
      }
      protected override void RemovePrimary(int primaryKey)
      {
        PrimaryCache.Remove(primaryKey);
      }
      protected override void ResolveCollision(int primaryKey, uint collisionBits)
      {
        KeyValuePair<byte[], ulong> secondaryCacheItem =
          SecondaryCache.First(k => BitConverter.ToInt32(k.Key, 0) == primaryKey);
        SecondaryCache.Remove(secondaryCacheItem.Key);

        if (!SecondaryCache.Keys.Any(key => BitConverter.ToInt32(key, 0) == primaryKey))
        {
          collisionBits &= ~((uint)1 << Address);
        }

        ulong uTXO = secondaryCacheItem.Value
          | ((ulong)collisionBits << COUNT_HEADERINDEX_BITS);

        PrimaryCache.Add(primaryKey, uTXO);
      }

      protected override void SpendSecondaryUTXO(byte[] key, int outputIndex, out bool areAllOutputpsSpent)
      {
        SpendUTXO(ref UTXOSecondaryExisting, outputIndex, out areAllOutputpsSpent);
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
        PrimaryCache[PrimaryKey] &= ~MasksCollision[cacheAddress];
      }

      static void SpendUTXO(ref ulong uTXO, int outputIndex, out bool areAllOutputpsSpent)
      {
        ulong mask = (ulong)1 << (CountHeaderPlusCollisionBits + outputIndex);
        if ((uTXO & mask) != 0x00)
        {
          throw new UTXOException(string.Format(
            "Output index {0} already spent.", outputIndex));
        }
        uTXO |= mask;

        areAllOutputpsSpent = (uTXO & MaskAllOutputBitsSpent) == MaskAllOutputBitsSpent;
      }

      protected override byte[] GetPrimaryData()
      {
        byte[] buffer = new byte[PrimaryCache.Count * 12];

        int index = 0;
        foreach (KeyValuePair<int, ulong> keyValuePair in PrimaryCache)
        {
          BitConverter.GetBytes(keyValuePair.Key).CopyTo(buffer, index);
          index += 4;
          BitConverter.GetBytes(keyValuePair.Value).CopyTo(buffer, index);
          index += 8;
        }

        return buffer;
      }
      protected override byte[] GetSecondaryData()
      {
        byte[] buffer = new byte[SecondaryCache.Count * (HASH_BYTE_SIZE + 8)];

        int index = 0;
        foreach (KeyValuePair<byte[], ulong> keyValuePair in SecondaryCache)
        {
          keyValuePair.Key.CopyTo(buffer, index);
          index += HASH_BYTE_SIZE;
          BitConverter.GetBytes(keyValuePair.Value).CopyTo(buffer, index);
          index += 8;
        }

        return buffer;
      }


      protected override void LoadPrimaryData(byte[] buffer)
      {
        int index = 0;

        while (index < buffer.Length)
        {
          int key = BitConverter.ToInt32(buffer, index);
          index += 4;
          ulong value = BitConverter.ToUInt64(buffer, index);
          index += 8;

          PrimaryCache.Add(key, value);
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

          ulong value = BitConverter.ToUInt64(buffer, index);
          index += 8;

          SecondaryCache.Add(key, value);
        }
      }

      public override void Clear()
      {
        PrimaryCache.Clear();
        SecondaryCache.Clear();
      }
    }
  }
}
