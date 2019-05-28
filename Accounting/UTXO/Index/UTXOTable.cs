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
      public int Address;
      public int OffsetCollisionBits;
      protected string Label;

      string DirectoryPath;


      public int PrimaryKey;

      protected UTXOTable(
        int address,
        string label)
      {
        Address = address;
        Label = label;

        OffsetCollisionBits = 
          COUNT_BATCHINDEX_BITS 
          + COUNT_HEADER_BITS 
          + address * COUNT_COLLISION_BITS_PER_TABLE;

        DirectoryPath = Path.Combine(RootPath, Label);
      }

      public abstract bool TryParseUTXO(
        int batchIndex,
        byte[] headerHash, 
        int countTXOutputs, 
        out UTXOItem uTXOIndexDataItem);

      public abstract bool PrimaryTableContainsKey(int primaryKey);
      public abstract void IncrementCollisionBits(int primaryKey, int collisionAddress);
      public abstract void SecondaryTableAddUTXO(UTXOItem uTXODataItem);
      public abstract void PrimaryTableAddUTXO(UTXOItem uTXODataItem);

      public abstract void SpendPrimaryUTXO(TXInput input, out bool areAllOutputpsSpent);
      public abstract bool TryGetValueInPrimaryTable(int primaryKey);
      public abstract bool HasCollision(int cacheAddress);
      public abstract void RemovePrimary();
      public abstract void ResolveCollision(UTXOTable tablePrimary);
      public abstract uint GetCollisionBits();
      public abstract bool AreCollisionBitsFull();

      public bool TrySpendCollision(
        TXInput input,
        UTXOTable tablePrimary)
      {
        if (TryGetValueInCollisionTable(input.TXIDOutput))
        {
          SpendCollisionUTXO(
            input.TXIDOutput, 
            input.OutputIndex,
            out bool allOutputsSpent);

          if (allOutputsSpent)
          {
            RemoveCollision(input.TXIDOutput);

            if (tablePrimary.AreCollisionBitsFull())
            {
              if (HasCountCollisions(
                input.PrimaryKeyTXIDOutput, 
                COUNT_COLLISIONS_MAX))
              {
                return true;
              }
            }

            tablePrimary.DecrementCollisionBits(Address);
            tablePrimary.UpdateUTXOInTable();
          }

          return true;
        }

        return false;
      }
      protected abstract void SpendCollisionUTXO(byte[] key, int outputIndex, out bool areAllOutputpsSpent);
      protected abstract bool TryGetValueInCollisionTable(byte[] key);
      protected abstract void RemoveCollision(byte[] key);
      protected abstract bool HasCountCollisions(int primaryKey, uint countCollisions);
      public abstract void DecrementCollisionBits(int tableAddress);
      protected abstract void UpdateUTXOInTable();

      protected abstract int GetCountPrimaryTableItems();
      protected abstract int GetCountSecondaryTableItems();

      public string GetLabelsMetricsCSV()
      {
        return Label + "PrimaryCache," + Label + "SecondaryCache";
      }
      public string GetMetricsCSV()
      {
        return GetCountPrimaryTableItems() + "," + GetCountSecondaryTableItems();
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
          GetCollisionData());
      }

      protected abstract byte[] GetPrimaryData();
      protected abstract byte[] GetCollisionData();


      protected abstract void LoadPrimaryData(byte[] buffer);
      protected abstract void LoadCollisionData(byte[] buffer);
      
      public abstract void Clear();

      public async Task LoadAsync()
      {
        LoadPrimaryData(
          await LoadFileAsync(
            Path.Combine(DirectoryPath, "PrimaryCache")));

        LoadCollisionData(
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
