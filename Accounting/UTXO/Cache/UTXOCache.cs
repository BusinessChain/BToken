using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.IO;
using System.Linq;

namespace BToken.Accounting
{
  public partial class UTXO
  {
    abstract class UTXOCache
    {
      UTXOCache MainCache;
      UTXOCache NextCache;

      protected string Label;
      string DirectoryPath;

      protected int Address;

      protected int PrimaryKey;


      protected UTXOCache(
        UTXOCache nextCache, 
        string label)
      {
        NextCache = nextCache;
        Label = label;
        DirectoryPath = Path.Combine(RootPath, Label);
      }

      public void Initialize()
      {
        var cache = this;
        int cacheAddress = 0;

        while(cache != null)
        {
          cache.MainCache = this;
          cache.Address = cacheAddress;

          cache = cache.NextCache;
          cacheAddress += 1;
        }
      }

      public void InsertUTXO(byte[] tXIDHash, byte[] headerHashBytes, int outputsCount)
      {
        int primaryKey = BitConverter.ToInt32(tXIDHash, 0);
        int lengthUTXOBits = CountHeaderPlusCollisionBits + outputsCount;

        var cache = this;

        while (cache != null)
        {
          if (cache.TryInsertUTXO(
            primaryKey,
            tXIDHash,
            headerHashBytes,
            lengthUTXOBits))
          {
            return;
          }

          cache = cache.NextCache;
        }

        throw new UTXOException("UTXO could not be inserted in Cache modules.");
      }
      bool TryInsertUTXO(
        int primaryKey,
        byte[] tXIDHash,
        byte[] headerHashBytes,
        int lengthUTXOBits)
      {
        if (IsUTXOTooLongForCache(lengthUTXOBits))
        {
          return false;
        }

        CreateUTXO(headerHashBytes, lengthUTXOBits);

        var cache = MainCache;

        while(cache != null)
        {
          if (cache.TrySetCollisionBit(primaryKey, Address))
          {
            SecondaryCacheAddUTXO(tXIDHash);
            return true;
          }

          cache = cache.NextCache;
        }

        PrimaryCacheAddUTXO(primaryKey);

        return true;
      }
      protected abstract bool IsUTXOTooLongForCache(int lengthUTXOBits);
      protected abstract void CreateUTXO(byte[] headerHashBytes, int lengthUTXOBits);
      protected abstract bool TrySetCollisionBit(int primaryKey, int collisionAddress);
      protected abstract void SecondaryCacheAddUTXO(byte[] tXIDHash);
      protected abstract void PrimaryCacheAddUTXO(int primaryKey);
                 
      public void SpendUTXO(byte[] tXIDOutput, int outputIndex)
      {
        int primaryKey = BitConverter.ToInt32(tXIDOutput, 0);
        
        var cache = this;

        while(cache != null)
        {
          if (cache.TrySpend(
            primaryKey, 
            tXIDOutput, 
            outputIndex))
          {
            return;
          }

          cache = cache.NextCache;
        }

        throw new UTXOException("Referenced TXID not found in UTXO table.");
      }

      bool TrySpend(
        int primaryKey,
        byte[] tXIDOutput,
        int outputIndex)
      {
        PrimaryKey = primaryKey;
        UTXOCache cacheCollision = null;

        if (TryGetValueInPrimaryCache(primaryKey))
        {
          uint collisionBits = 0;
          var cache = MainCache;

          while (cache != null)
          {
            if (IsCollision(cache.Address))
            {
              cacheCollision = cache;

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

      protected abstract int GetCountPrimaryCacheItems();
      protected abstract int GetCountSecondaryCacheItems();

      public string GetLabelsMetricsCSV()
      {
        string labels = Label + "PrimaryCache," + Label + "SecondaryCache";

        UTXOCache cache = NextCache;
        while (cache != null)
        {
          labels +=
            "," + cache.Label + "PrimaryCache" +
            "," + cache.Label + "SecondaryCache";

          cache = cache.NextCache;
        }

        return labels;
      }
      public string GetMetricsCSV()
      {
        string metrics = GetCountPrimaryCacheItems() + "," + GetCountSecondaryCacheItems();

        UTXOCache cache = NextCache;
        while (cache != null)
        {
          metrics +=
            "," + cache.GetCountPrimaryCacheItems() +
            "," + cache.GetCountSecondaryCacheItems();

          cache = cache.NextCache;
        }

        return metrics;
      }

      public void BackupToDisk()
      {
        List<UTXOCache> caches = new List<UTXOCache>();
        var cache = this;

        while (cache != null)
        {
          caches.Add(cache);
          cache = cache.NextCache;
        }

        Parallel.ForEach(caches, c => c.Archive());
      }
      void Archive()
      {
        string directoryPath = Path.Combine(RootPath, Label);
        Directory.CreateDirectory(directoryPath);
        
        Task writeToFileTask = WriteFileAsync(
          Path.Combine(directoryPath, "PrimaryCache"), 
          GetPrimaryData());
        
        writeToFileTask = WriteFileAsync(
          Path.Combine(directoryPath, "SecondaryCache"), 
          GetSecondaryData());
      }

      protected abstract byte[] GetPrimaryData();
      protected abstract byte[] GetSecondaryData();

      public async Task LoadFromDiskAsync()
      {
        List<UTXOCache> caches = new List<UTXOCache>();
        var cache = this;

        while (cache != null)
        {
          caches.Add(cache);
          cache = cache.NextCache;
        }
        var loadTasks = caches
          .Select(c => { return c.LoadAsync(); });

        await Task.WhenAll(loadTasks);
      }

      async Task LoadAsync()
      {
        LoadPrimaryData(
          await LoadFileAsync(
            Path.Combine(DirectoryPath, "PrimaryCache")));

        LoadSecondaryData(
          await LoadFileAsync(
            Path.Combine(DirectoryPath, "SecondaryCache")));
      }

      protected abstract void LoadPrimaryData(byte[] buffer);
      protected abstract void LoadSecondaryData(byte[] buffer);


      public abstract void Clear();

    }
  }
}
