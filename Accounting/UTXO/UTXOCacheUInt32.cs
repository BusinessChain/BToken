using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
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

      static readonly uint MaskAllOutputBitsSpent = uint.MaxValue << CountHeaderPlusCollisionBits;
      static readonly int CountNonHeaderBits = COUNT_INTEGER_BITS - COUNT_HEADERINDEX_BITS;

      uint[] MasksCollision = {
        0x04000000,
        0x08000000,
        0x10000000 };


      public UTXOCacheUInt32(UTXOCache nextCache) 
        : base(
            nextCache,
            "PrimaryCacheUInt32",
            "SecondaryCacheUInt32"
            )
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

        for (int i = CountHeaderBytes; i > 0; i--)
        {
          UTXOIndex <<= 8;
          UTXOIndex |= headerHashBytes[i - 1];
        }
        UTXOIndex <<= CountNonHeaderBits;
        UTXOIndex >>= CountNonHeaderBits;

        int countUTXORemainderBits = lengthUTXOBits % 8;
        if (countUTXORemainderBits > 0)
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

    }
  }
}
