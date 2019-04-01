using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace BToken.Accounting
{
  public partial class UTXO
  {
    abstract class UTXOCache
    {
      UTXOCache[] Caches;

      protected int PrimaryKey;
      protected int CollisionCacheIndex;


      public UTXOCache(UTXOCache[] caches)
      {
        Caches = caches;
      }

      public abstract bool TrySetCollisionBit(int primaryKey, int collisionIndex);

      public bool TrySpend(
        int primaryKey,
        byte[] tXIDOutput,
        int outputIndex)
      {
        PrimaryKey = primaryKey;
        CollisionCacheIndex = 0;
        uint collisionBits = 0;
        UTXOCache cacheCollision = null;

        if (TryGetValueInPrimaryCache(primaryKey))
        {
          while (CollisionCacheIndex < Caches.Length)
          {
            if(IsCollision(CollisionCacheIndex))
            {
              collisionBits |= (uint)(1 << CollisionCacheIndex);
              cacheCollision = Caches[CollisionCacheIndex];

              if (cacheCollision.TrySpendSecondary(
                primaryKey,
                tXIDOutput,
                outputIndex,
                this))
              {
                return true;
              }
            }

            CollisionCacheIndex++;
          }

          SpendPrimaryUTXO(outputIndex, out bool areAllOutputpsSpent);

          if (areAllOutputpsSpent)
          {
            RemovePrimary(primaryKey);

            if (cacheCollision != null)
            {
              cacheCollision.ResolveCollision(primaryKey, collisionBits);
            }
          }

          return true;
        }
        else
        {
          return false;
        }
      }
      protected abstract void SpendPrimaryUTXO(int outputIndex, out bool areAllOutputpsSpent);
      protected abstract bool TryGetValueInPrimaryCache(int primaryKey);
      protected abstract bool IsCollision(int collisionCacheIndex);
      protected abstract void RemovePrimary(int primaryKey);
      protected abstract void ResolveCollision(int primaryKey, uint collisionBits);
      
      bool TrySpendSecondary(
        int primaryKey,
        byte[] tXIDOutput,
        int outputIndex,
        UTXOCache primaryCache)
      {
        if (TryGetValueInSecondaryCache(tXIDOutput))
        {
          SpendSecondaryUTXO(tXIDOutput, outputIndex, out bool areAllOutputpsSpent);

          if (areAllOutputpsSpent)
          {
            RemoveSecondary(primaryKey, tXIDOutput, out bool hasMoreCollisions);

            if (!hasMoreCollisions)
            {
              primaryCache.ClearCollisionBit();
            }
          }

          return true;
        }
        else
        {
          return false;
        }
      }
      protected abstract void SpendSecondaryUTXO(byte[] key, int outputIndex, out bool areAllOutputpsSpent);
      protected abstract bool TryGetValueInSecondaryCache(byte[] key);
      protected abstract void RemoveSecondary(int primaryKey, byte[] key, out bool hasMoreCollisions);
      protected abstract void ClearCollisionBit();

      public abstract int GetCountPrimaryCacheItems();
      public abstract int GetCountSecondaryCacheItems();
    }
  }
}
