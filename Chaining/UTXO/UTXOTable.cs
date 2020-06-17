using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using System.Linq;



namespace BToken.Chaining
{
  public partial class UTXOTable
  {
    byte[] GenesisBlockBytes;

    const int HASH_BYTE_SIZE = 32;

    const int COUNT_BATCHINDEX_BITS = 16;
    const int COUNT_COLLISION_BITS_PER_TABLE = 2;
    const int COUNT_COLLISIONS_MAX = 2 ^ COUNT_COLLISION_BITS_PER_TABLE - 1;

    const int LENGTH_BITS_UINT = 32;
    const int LENGTH_BITS_ULONG = 64;

    const int COUNT_NON_OUTPUT_BITS =
      COUNT_BATCHINDEX_BITS +
      COUNT_COLLISION_BITS_PER_TABLE * 3;

    UTXOIndexCompressed[] Tables;
    UTXOIndexUInt32Compressed TableUInt32 = new UTXOIndexUInt32Compressed();
    UTXOIndexULong64Compressed TableULong64 = new UTXOIndexULong64Compressed();
    UTXOIndexUInt32ArrayCompressed TableUInt32Array = new UTXOIndexUInt32ArrayCompressed();

    
    public int HeightBlockchain;
    public int IndexBlockArchive;

    long UTCTimeStartMerger;



    public UTXOTable(byte[] genesisBlockBytes)
    {
      Tables = new UTXOIndexCompressed[]{
        TableUInt32,
        TableULong64,
        TableUInt32Array };

      GenesisBlockBytes = genesisBlockBytes;
    }



    void InsertUTXO(
      byte[] uTXOKey,
      UTXOIndexCompressed table)
    {
      int primaryKey = BitConverter.ToInt32(uTXOKey, 0);

      for (int c = 0; c < Tables.Length; c += 1)
      {
        if (Tables[c].PrimaryTableContainsKey(primaryKey))
        {
          Tables[c].IncrementCollisionBits(
            primaryKey,
            table.Address);

          table.AddUTXOAsCollision(uTXOKey);

          return;
        }
      }

      table.AddUTXOAsPrimary(primaryKey);
    }

    void InsertUTXOsUInt32(
      KeyValuePair<byte[], uint>[] uTXOsUInt32,
      int archiveIndex)
    {
      int i = 0;

      while (i < uTXOsUInt32.Length)
      {
        TableUInt32.UTXO =
          uTXOsUInt32[i].Value |
          ((uint)archiveIndex & UTXOIndexUInt32.MaskBatchIndex);

        InsertUTXO(
          uTXOsUInt32[i].Key,
          TableUInt32);

        i += 1;
      }
    }

    void InsertUTXOsULong64(
      KeyValuePair<byte[], ulong>[] uTXOsULong64,
      int archiveIndex)
    {
      int i = 0;

      while (i < uTXOsULong64.Length)
      {
        TableULong64.UTXO =
          uTXOsULong64[i].Value |
          ((ulong)archiveIndex & UTXOIndexULong64.MaskBatchIndex);

        InsertUTXO(
          uTXOsULong64[i].Key,
          TableULong64);

        i += 1;
      }
    }

    void InsertUTXOsUInt32Array(
      KeyValuePair<byte[], uint[]>[] uTXOsUInt32Array,
      int archiveIndex)
    {
      int i = 0;

      while (i < uTXOsUInt32Array.Length)
      {
        TableUInt32Array.UTXO = uTXOsUInt32Array[i].Value;
        TableUInt32Array.UTXO[0] |=
          (uint)archiveIndex & UTXOIndexUInt32Array.MaskBatchIndex;

        InsertUTXO(
          uTXOsUInt32Array[i].Key,
          TableUInt32Array);

        i += 1;
      }
    }

    void InsertSpendUTXOs(List<TXInput> inputs)
    {
      int i = 0;
    LoopSpendUTXOs:
      while (i < inputs.Count)
      {
        for (int c = 0; c < Tables.Length; c += 1)
        {
          UTXOIndexCompressed tablePrimary = Tables[c];

          if (tablePrimary.TryGetValueInPrimaryTable(inputs[i].PrimaryKeyTXIDOutput))
          {
            UTXOIndexCompressed tableCollision = null;
            for (int cc = 0; cc < Tables.Length; cc += 1)
            {
              if (tablePrimary.HasCollision(cc))
              {
                tableCollision = Tables[cc];

                if (tableCollision.TrySpendCollision(inputs[i], tablePrimary))
                {
                  i += 1;
                  goto LoopSpendUTXOs;
                }
              }
            }

            tablePrimary.SpendPrimaryUTXO(inputs[i], out bool allOutputsSpent);

            if (allOutputsSpent)
            {
              tablePrimary.RemovePrimary();

              if (tableCollision != null)
              {
                tableCollision.ResolveCollision(tablePrimary);
              }
            }

            i += 1;
            goto LoopSpendUTXOs;
          }
        }

        throw new ChainException(
          string.Format(
            "Referenced TX {0} not found in UTXO table.",
            inputs[i].TXIDOutput.ToHexString()),
          ErrorCode.INVALID);
      }
    }


    string PathUTXOState = "UTXOArchive";

    public bool TryLoadImage(int indexMax)
    {
      // zuerst utxoStatus vom neuen image laden 
      // falls grösser als indexMax, dann status vom alten 
      // image laden falls grösser dann reset und return false,
      // andernfalls laden und return true
    }

    public void LoadImage()
    {
      LoadMapBlockToArchiveData(
        File.ReadAllBytes(
          Path.Combine(PathUTXOState, "MapBlockHeader")));

      for (int c = 0; c < Tables.Length; c += 1)
      {
        Tables[c].Load();
      }

      Console.WriteLine(
        "Load UTXO Image from {0}",
        PathUTXOState);
    }

    public void Clear()
    {
      for (int c = 0; c < Tables.Length; c += 1)
      {
        Tables[c].Clear();
      }
    }


    // Similar function as LoadCollisionData in UTXOIndexUInt32Compressed
    public void LoadMapBlockToArchiveData(byte[] buffer)
    {
      int index = 0;

      while (index < buffer.Length)
      {
        byte[] key = new byte[HASH_BYTE_SIZE];
        Array.Copy(buffer, index, key, 0, HASH_BYTE_SIZE);
        index += HASH_BYTE_SIZE;

        int value = BitConverter.ToInt32(buffer, index);
        index += 4;

        Synchronizer.MapBlockToArchiveIndex.Add(key, value);
      }
    }



    public void InsertBlockArchive(
      BlockArchive blockArchive)
    {
      blockArchive.StopwatchStaging.Start();

      InsertUTXOsUInt32(
        blockArchive.UTXOsUInt32,
        blockArchive.Index);

      InsertUTXOsULong64(
        blockArchive.UTXOsULong64,
        blockArchive.Index);

      InsertUTXOsUInt32Array(
        blockArchive.UTXOsUInt32Array,
        blockArchive.Index);

      InsertSpendUTXOs(blockArchive.Inputs);

      blockArchive.StopwatchStaging.Stop();
      
      HeightBlockchain += blockArchive.BlockCount;

      LogInsertion(blockArchive);
    }


    public void ArchiveImage(int archiveIndex, int height)
    {
      Directory.CreateDirectory(PathUTXOState);

      byte[] utxoStateBytes = new byte[8];

      BitConverter.GetBytes(archiveIndex)
        .CopyTo(utxoStateBytes, 0);
      BitConverter.GetBytes(height)
        .CopyTo(utxoStateBytes, 4);

      using (FileStream stream = new FileStream(
         Path.Combine(PathUTXOState, "UTXOState"),
         FileMode.Create,
         FileAccess.Write))
      {
        stream.Write(
          utxoStateBytes, 
          0,
          utxoStateBytes.Length);
      }

      using (FileStream stream = new FileStream(
         Path.Combine(PathUTXOState, "MapBlockHeader"),
         FileMode.Create,
         FileAccess.Write))
      {
        foreach (KeyValuePair<byte[], int> keyValuePair
          in MapBlockToArchiveIndex)
        {
          stream.Write(keyValuePair.Key, 0, keyValuePair.Key.Length);

          byte[] valueBytes = BitConverter.GetBytes(keyValuePair.Value);
          stream.Write(valueBytes, 0, valueBytes.Length);
        }
      }

      Backup();
    }

    public void Backup()
    {
      Parallel.ForEach(Tables, t =>
      {
        t.BackupImage(PathUTXOState);
      });
    }
    
    public void Restore()
    {
      throw new NotImplementedException();
    }


    void LogInsertion(BlockArchive container)
    {
      if (UTCTimeStartMerger == 0)
      {
        UTCTimeStartMerger = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
      }

      int ratioMergeToParse =
        (int)((float)container.StopwatchStaging.ElapsedTicks * 100
        / container.StopwatchParse.ElapsedTicks);

      int countOutputs =
        container.UTXOsUInt32.Length +
        container.UTXOsULong64.Length +
        container.UTXOsUInt32Array.Length;

      string logCSV = string.Format(
        "{0},{1},{2},{3},{4},{5},{6},{7},{8},{9}",
        container.Index,
        HeightBlockchain,
        DateTimeOffset.UtcNow.ToUnixTimeSeconds() - UTCTimeStartMerger,
        ratioMergeToParse,
        container.Buffer.Length,
        container.Inputs.Count,
        countOutputs,
        Tables[0].GetMetricsCSV(),
        Tables[1].GetMetricsCSV(),
        Tables[2].GetMetricsCSV());

      Console.WriteLine(logCSV);
    }
  }
}
