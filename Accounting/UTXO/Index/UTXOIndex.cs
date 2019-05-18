using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.IO;
using System.Linq;

namespace BToken.Accounting
{
  public partial class UTXO
  {
    abstract class UTXOIndex
    {
      protected string Label;
      string DirectoryPath;

      public int Address;


      protected UTXOIndex(int address, string label)
      {
        Label = label;
        Address = address;
        DirectoryPath = Path.Combine(RootPath, Label);
      }

      public abstract bool TryParseUTXO(
        byte[] headerHash, 
        int lengthUTXOBits, 
        out UTXODataItem uTXOIndexDataItem);

      public abstract bool IsUTXOTooLongForCache(int lengthUTXOBits);
      public abstract void CreateUTXO(byte[] headerHashBytes, int lengthUTXOBits);
      public abstract bool TrySetCollisionBit(int primaryKey, int collisionAddress);
      public abstract void SecondaryCacheAddUTXO(byte[] tXIDHash);
      public abstract void PrimaryCacheAddUTXO(int primaryKey);
         
      public abstract void SpendPrimaryUTXO(int primaryKey, int outputIndex, out bool areAllOutputpsSpent);
      public abstract bool TryGetValueInPrimaryCache(int primaryKey);
      public abstract bool IsCollision(int cacheAddress);
      public abstract void RemovePrimary(int primaryKey);
      public abstract void ResolveCollision(int primaryKey, uint collisionBits);
      
      public bool TrySpendSecondary(
        int primaryKey,
        byte[] tXIDOutput,
        int outputIndex,
        UTXOIndex primaryCache)
      {
        if (TryGetValueInSecondaryCache(tXIDOutput))
        {
          SpendSecondaryUTXO(tXIDOutput, outputIndex, out bool areAllOutputpsSpent);

          if (areAllOutputpsSpent)
          {
            RemoveSecondary(primaryKey, tXIDOutput, out bool hasMoreCollisions);

            if (!hasMoreCollisions)
            {
              primaryCache.ClearCollisionBit(primaryKey, Address);
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
      protected abstract void ClearCollisionBit(int primaryKey, int cacheAddress);

      protected abstract int GetCountPrimaryCacheItems();
      protected abstract int GetCountSecondaryCacheItems();

      public string GetLabelsMetricsCSV()
      {
        return Label + "PrimaryCache," + Label + "SecondaryCache";
      }
      public string GetMetricsCSV()
      {
        return GetCountPrimaryCacheItems() + "," + GetCountSecondaryCacheItems();
      }

      public void BackupToDisk()
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


      protected abstract void LoadPrimaryData(byte[] buffer);
      protected abstract void LoadSecondaryData(byte[] buffer);
      
      public abstract void Clear();

      public async Task LoadAsync()
      {
        LoadPrimaryData(
          await LoadFileAsync(
            Path.Combine(DirectoryPath, "PrimaryCache")));

        LoadSecondaryData(
          await LoadFileAsync(
            Path.Combine(DirectoryPath, "SecondaryCache")));
      }
    }

    abstract class UTXODataItem
    {
      public int PrimaryKey;
      public byte[] Hash;

      public UTXODataItem()
      { }
    }
  }
}
