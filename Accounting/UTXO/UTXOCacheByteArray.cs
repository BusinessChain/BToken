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
      
      byte[] MasksCollisionCaches = {
        0x04,
        0x08,
        0x10}; // make this parametric


      public UTXOCacheByteArray(UTXOCache[] caches) : base(caches)
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
        if (PrimaryCache.TryGetValue(primaryKey, out byte[] uTXOExisting))
        {
          uTXOExisting[ByteIndexCollisionBits] |= MasksCollisionCaches[collisionIndex];

          return true;
        }

        return false;
      }
      public void Write(int primaryKey, byte[] uTXO)
      {
        PrimaryCache.Add(primaryKey, uTXO);
      }
      public void Write(byte[] tXIDHash, byte[] uTXO)
      {
        SecondaryCache.Add(tXIDHash, uTXO);
      }

      protected override void SpendPrimaryUTXO(int outputIndex, out bool areAllOutputpsSpent)
      {
        SpendUTXO(UTXOPrimaryExisting, outputIndex, out areAllOutputpsSpent);
      }
      protected override bool TryGetValueInPrimaryCache(int primaryKey)
      {
        return PrimaryCache.TryGetValue(primaryKey, out UTXOPrimaryExisting);
      }
      protected override bool IsCollision(int collisionCacheIndex)
      {
        return 
          (MasksCollisionCaches[collisionCacheIndex] & 
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
      protected override void ClearCollisionBit()
      {
        UTXOPrimaryExisting[ByteIndexCollisionBits] &= (byte)~MasksCollisionCaches[CollisionCacheIndex];
      }

      static void SpendUTXO(byte[] uTXO, int outputIndex, out bool areAllOutputpsSpent)
      {
        byte mask = (byte)(1 << (CountHeaderBitsInByte + outputIndex));
        if ((uTXO[ByteIndexCollisionBits] & mask) != 0x00)
        {
          throw new UTXOException(string.Format(
            "Output index {0} already spent.", outputIndex));
        }
        uTXO[ByteIndexCollisionBits] |= mask;

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
