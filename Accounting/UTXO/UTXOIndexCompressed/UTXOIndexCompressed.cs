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
      protected string Label;

      protected string DirectoryPath;

      protected uint MaskCollisionBits =
        (3 << COUNT_BATCHINDEX_BITS + COUNT_COLLISION_BITS_PER_TABLE * 0) |
        (3 << COUNT_BATCHINDEX_BITS + COUNT_COLLISION_BITS_PER_TABLE * 1) |
        (3 << COUNT_BATCHINDEX_BITS + COUNT_COLLISION_BITS_PER_TABLE * 2);


      public int PrimaryKey;


      protected UTXOIndexCompressed(
        int address,
        string label)
      {
        Address = address;
        Label = label;

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

      public abstract void BackupToDisk(string path);
      public abstract void Load();

      public abstract void Clear();

    }
  }
}
