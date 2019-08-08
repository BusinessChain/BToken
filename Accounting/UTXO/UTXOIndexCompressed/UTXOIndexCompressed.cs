using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.IO;
using System.Diagnostics;

namespace BToken.Accounting
{
  public partial class UTXO
  {
    abstract class UTXOIndexCompressed
    {
      public int Address;
      public int OffsetCollisionBits;
      protected string Label;

      string DirectoryPath;

      protected static readonly uint MaskCollisionBits = 0x03F00000;
      
      public int PrimaryKey;


      protected UTXOIndexCompressed(
        int address,
        string label)
      {
        Address = address;
        Label = label;

        OffsetCollisionBits = 
          COUNT_BATCHINDEX_BITS 
          + COUNT_HEADER_BITS 
          + address * COUNT_COLLISION_BITS_PER_TABLE;

        DirectoryPath = Path.Combine(PathUTXOState, Label);
      }

      public abstract bool PrimaryTableContainsKey(int primaryKey);
      public abstract void IncrementCollisionBits(int primaryKey, int collisionAddress);

      public abstract void SpendPrimaryUTXO(in TXInput input, out bool areAllOutputpsSpent);
      public abstract bool TryGetValueInPrimaryTable(int primaryKey);
      public abstract bool HasCollision(int cacheAddress);
      public abstract void RemovePrimary();
      public abstract void ResolveCollision(UTXOIndexCompressed tablePrimary);
      public abstract uint GetCollisionBits();
      public abstract bool AreCollisionBitsFull();

      public bool TrySpendCollision(
        in TXInput input,
        UTXOIndexCompressed tablePrimary)
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
      protected abstract int GetCountCollisionTableItems();

      public string GetMetricsCSV()
      {
        return GetCountPrimaryTableItems() + "," + GetCountCollisionTableItems();
      }

      public async Task BackupToDiskAsync(string path)
      {
        string directoryPath = Path.Combine(path, Label);
        Directory.CreateDirectory(directoryPath);

        Task[] writeToFileTasks = new Task[2];

        writeToFileTasks[0] = WriteFileAsync(
          Path.Combine(directoryPath, "PrimaryTable"),
          GetPrimaryData());

        writeToFileTasks[1] = WriteFileAsync(
          Path.Combine(directoryPath, "CollisionTable"),
          GetCollisionData());

        await Task.WhenAll(writeToFileTasks);
      }
      public void BackupToDisk(string path)
      {
        string directoryPath = Path.Combine(path, Label);
        Directory.CreateDirectory(directoryPath);
        
        WriteFile(
          Path.Combine(directoryPath, "PrimaryTable"),
          GetPrimaryData());

        WriteFile(
          Path.Combine(directoryPath, "CollisionTable"),
          GetCollisionData());
      }

      static void WriteFile(string filePath, byte[] buffer)
      {
        using (FileStream stream = new FileStream(
           filePath,
           FileMode.Create,
           FileAccess.ReadWrite,
           FileShare.Read))
        {
          stream.Write(buffer, 0, buffer.Length);
        }
      }
      static async Task WriteFileAsync(string filePath, byte[] buffer)
      {
        using (FileStream stream = new FileStream(
           filePath,
           FileMode.Create,
           FileAccess.ReadWrite,
           FileShare.Read,
           bufferSize: 1048576,
           useAsync: true))
        {
          await stream.WriteAsync(buffer, 0, buffer.Length).ConfigureAwait(false);
        }
      }

      protected abstract byte[] GetPrimaryData();
      protected abstract byte[] GetCollisionData();
      
      protected abstract void LoadPrimaryData(byte[] buffer);
      protected abstract void LoadCollisionData(byte[] buffer);
      
      public abstract void Clear();

      public void Load()
      {
        LoadPrimaryData(
          File.ReadAllBytes(
            Path.Combine(DirectoryPath, "PrimaryTable")));

        LoadCollisionData(
          File.ReadAllBytes(
            Path.Combine(DirectoryPath, "CollisionTable")));
      }
    }
  }
}
