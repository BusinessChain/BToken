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

      uint UTXOPrimaryExisting;
      uint UTXOSecondaryExisting;

      uint[] MasksCollisionCaches = {
        0x04000000,
        0x08000000,
        0x10000000};


      public UTXOCacheUInt32(UTXOCache[] caches) : base(caches)
      { }


      public override int GetCountPrimaryCacheItems()
      {
        return PrimaryCache.Count;
      }
      public override int GetCountSecondaryCacheItems()
      {
        return SecondaryCache.Count;
      }

      public override bool TrySetCollisionBit(int primaryKey, int collisionIndex)
      {
        if(PrimaryCache.ContainsKey(primaryKey))
        {
          PrimaryCache[primaryKey] |= MasksCollisionCaches[collisionIndex];

          return true;
        }

        return false;
      }

      public void Write(int primaryKey, uint uTXO)
      {
        PrimaryCache.Add(primaryKey, uTXO);
      }
      public void Write(byte[] tXIDHash, uint uTXO)
      {
        SecondaryCache.Add(tXIDHash, uTXO);
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
      protected override bool IsCollision(int collisionCacheIndex)
      {
        return (MasksCollisionCaches[collisionCacheIndex] & UTXOPrimaryExisting) != 0;
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
          collisionBits &= ~((uint)1 << IndexCacheUInt32);
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
      protected override void ClearCollisionBit()
      {
        UTXOPrimaryExisting &= ~MasksCollisionCaches[CollisionCacheIndex];
        PrimaryCache[PrimaryKey] = UTXOPrimaryExisting;
      }
      
      static void SpendUTXO(ref uint uTXO, int outputIndex, out bool areAllOutputpsSpent)
      {
        uint mask = (uint)1 << (CountHeaderPlusCollisionBits + outputIndex);
        if ((uTXO & mask) != 0x00)
        {
          throw new UTXOException(string.Format(
            "Output index '{0}' already spent.", outputIndex));
        }
        uTXO |= mask;

        areAllOutputpsSpent = (uTXO & MaskAllOutputBitsSpent) == MaskAllOutputBitsSpent;
      }

    }
  }
}
