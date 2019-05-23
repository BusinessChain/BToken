using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.IO;
using System.Linq;

namespace BToken.Accounting
{
  public partial class UTXO
  {
    abstract class UTXOTable
    {
      protected string Label;
      string DirectoryPath;

      public int Address;


      protected UTXOTable(int address, string label)
      {
        Label = label;
        Address = address;
        DirectoryPath = Path.Combine(RootPath, Label);
      }

      public abstract bool TryParseUTXO(
        byte[] headerHash, 
        int lengthUTXOBits, 
        out UTXOItem uTXOIndexDataItem);

      public abstract bool TrySetCollisionBit(int primaryKey, int collisionAddress);
      public abstract void SecondaryCacheAddUTXO(UTXOItem uTXODataItem);
      public abstract void PrimaryCacheAddUTXO(UTXOItem uTXODataItem);

      public abstract void SpendPrimaryUTXO(TXInput input, out bool areAllOutputpsSpent);
      public abstract bool TryGetValueInPrimaryCache(int primaryKey);
      public abstract bool IsCollision(int cacheAddress);
      public abstract void RemovePrimary(int primaryKey);
      public abstract void ResolveCollision(int primaryKey, uint collisionBits);
      
      public bool TrySpendSecondary(
        TXInput input,
        UTXOTable primaryCache)
      {
        if (TryGetValueInSecondaryCache(input.TXIDOutput))
        {
          SpendSecondaryUTXO(input.TXIDOutput, input.OutputIndex, out bool areAllOutputpsSpent);

          if (areAllOutputpsSpent)
          {
            RemoveSecondary(input.PrimaryKeyTXIDOutput, input.TXIDOutput, out bool hasMoreCollisions);

            if (!hasMoreCollisions)
            {
              primaryCache.ClearCollisionBit(input.PrimaryKeyTXIDOutput, Address);
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

    abstract class UTXOItem
    {
      public int PrimaryKey;
      public byte[] Hash;

      public UTXOItem()
      { }
    }
  }
}
