using System;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using System.Threading.Tasks;

namespace BToken.Accounting
{
  public partial class UTXO
  {
    class UTXOCacheUInt32 : UTXOCache
    {
      Dictionary<int, uint> PrimaryCache = new Dictionary<int, uint>();
      Dictionary<byte[], uint> SecondaryCache = 
        new Dictionary<byte[], uint>(new EqualityComparerByteArray());

      uint UTXOIndex;
      uint UTXOPrimaryExisting;
      uint UTXOSecondaryExisting;

      const int COUNT_INTEGER_BITS = sizeof(int) * 8;

      static readonly uint MaskAllOutputBitsSpent = uint.MaxValue << CountHeaderPlusCollisionBits;
      static readonly int CountNonHeaderBits = COUNT_INTEGER_BITS - COUNT_HEADERINDEX_BITS;
      static readonly int CountHeaderBytes = (COUNT_HEADERINDEX_BITS + 7) / 8;

      uint[] MasksCollision = {
        0x04000000,
        0x08000000,
        0x10000000 };


      public UTXOCacheUInt32(UTXOCache nextCache)
        : base(nextCache, "UInt32")
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
        return COUNT_INTEGER_BITS < lengthUTXOBits;
      }
      protected override void CreateUTXO(byte[] headerHashBytes, int lengthUTXOBits)
      {
        UTXOIndex = 0;

        for (int i = CountHeaderBytes; i > 0; i -= 1)
        {
          UTXOIndex <<= 8;
          UTXOIndex |= headerHashBytes[i - 1];
        }
        UTXOIndex <<= CountNonHeaderBits;
        UTXOIndex >>= CountNonHeaderBits;

        if (COUNT_INTEGER_BITS > lengthUTXOBits)
        {
          UTXOIndex |= (uint.MaxValue << lengthUTXOBits);
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
        KeyValuePair<byte[], uint> secondaryCacheItem =
          SecondaryCache.First(k => BitConverter.ToInt32(k.Key, 0) == primaryKey);
        SecondaryCache.Remove(secondaryCacheItem.Key);

        if (!SecondaryCache.Keys.Any(key => BitConverter.ToInt32(key, 0) == primaryKey))
        {
          collisionBits &= ~((uint)1 << Address);
        }

        uint uTXO = secondaryCacheItem.Value
          | (collisionBits << COUNT_HEADERINDEX_BITS);

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
      
      static void SpendUTXO(ref uint uTXO, int outputIndex, out bool areAllOutputpsSpent)
      {
        uint mask = (uint)1 << (CountHeaderPlusCollisionBits + outputIndex);
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
        byte[] buffer = new byte[PrimaryCache.Count << 3];

        int index = 0;
        foreach(KeyValuePair<int, uint> keyValuePair in PrimaryCache)
        {
          BitConverter.GetBytes(keyValuePair.Key).CopyTo(buffer, index);
          index += 4;
          BitConverter.GetBytes(keyValuePair.Value).CopyTo(buffer, index);
          index += 4;
        }

        return buffer;
      }
      protected override byte[] GetSecondaryData()
      {
        byte[] buffer = new byte[SecondaryCache.Count * (HASH_BYTE_SIZE + 4)];

        int index = 0;
        foreach (KeyValuePair<byte[], uint> keyValuePair in SecondaryCache)
        {
          keyValuePair.Key.CopyTo(buffer, index);
          index += HASH_BYTE_SIZE;
          BitConverter.GetBytes(keyValuePair.Value).CopyTo(buffer, index);
          index += 4;
        }

        return buffer;
      }

      protected override void LoadPrimaryData(byte[] buffer)
      {
        int index = 0;

        while(index < buffer.Length)
        {
          int key = BitConverter.ToInt32(buffer, index);
          index += 4;
          uint value = BitConverter.ToUInt32(buffer, index);
          index += 4;

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

          uint value = BitConverter.ToUInt32(buffer, index);
          index += 4;

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
