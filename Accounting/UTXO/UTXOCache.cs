using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace BToken.Accounting
{
  public partial class UTXO
  {
    abstract class UTXOCache
    {
      UTXOCache NextCache;

      protected int Address;
      protected int PrimaryKey;


      public UTXOCache(int address, UTXOCache nextCache)
      {
        Address = address;
        NextCache = nextCache;
      }
      
      protected abstract bool TryInsertUTXO(int primaryKey, byte[] tXIDHash, byte[] headerHashBytes, int lengthUTXOBits);

      public void InsertUTXO(byte[] tXIDHash, byte[] headerHashBytes, int outputsCount)
      {
        int primaryKey = BitConverter.ToInt32(tXIDHash, 0);
        int lengthUTXOBits = CountHeaderPlusCollisionBits + outputsCount;

        if(TryInsertUTXO(primaryKey, tXIDHash, headerHashBytes, lengthUTXOBits))
        {
          return;
        }
        
        if (NextCache != null
          && NextCache.TryInsertUTXO(primaryKey, tXIDHash, headerHashBytes, lengthUTXOBits))
        {
          return;
        }

        var cache = NextCache.NextCache;
        while (cache != null)
        {
          if (cache.TryInsertUTXO(primaryKey, tXIDHash, headerHashBytes, lengthUTXOBits))
          {
            return;
          }

          cache = cache.NextCache;
        }

        throw new UTXOException("UTXO could not be inserted attached Cache modules.");
      }

      public void SpendUTXO(byte[] tXIDOutput, int outputIndex)
      {
        int primaryKey = BitConverter.ToInt32(tXIDOutput, 0);

        if(TrySpend(primaryKey, tXIDOutput, outputIndex))
        {
          return;
        }

        if(NextCache != null
          && NextCache.TrySpend(primaryKey, tXIDOutput, outputIndex))
        {
          return;
        }

        var cache = NextCache.NextCache;
        while(cache != null)
        {
          if (cache.TrySpend(primaryKey, tXIDOutput, outputIndex))
          {
            return;
          }

          cache = cache.NextCache;
        }

        throw new UTXOException("Referenced TXID not found in UTXO table.");
      }

      public bool TrySpend(
        int primaryKey,
        byte[] tXIDOutput,
        int outputIndex)
      {
        PrimaryKey = primaryKey;
        UTXOCache cacheCollision = null;

        if (TryGetValueInPrimaryCache(primaryKey))
        {
          uint collisionBits = 0;
          var cache = this;

          while (cache != null)
          {
            if (IsCollision(cache.Address))
            {
              if(cacheCollision == null)
              {
                cacheCollision = cache;
              }
              collisionBits |= (uint)(1 << cache.Address);

              if (cache.TrySpendSecondary(
                primaryKey,
                tXIDOutput,
                outputIndex,
                this))
              {
                return true;
              }
            }

            cache = cache.NextCache;
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
      protected abstract bool IsCollision(int cacheAddress);
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
              primaryCache.ClearCollisionBit(Address);
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
      protected abstract void ClearCollisionBit(int cacheAddress);

      public abstract int GetCountPrimaryCacheItems();
      public abstract int GetCountSecondaryCacheItems();
    }
  }
}
