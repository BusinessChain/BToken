using System;
using System.Collections.Generic;
using System.IO;
using System.Diagnostics;
using System.Threading.Tasks;
using System.Linq;

using BToken.Networking;



namespace BToken.Chaining
{
  partial class UTXOTable
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

    Headerchain Headerchain;
    UTXOIndexCompressed[] Tables;
    UTXOIndexUInt32Compressed TableUInt32 = new UTXOIndexUInt32Compressed();
    UTXOIndexULong64Compressed TableULong64 = new UTXOIndexULong64Compressed();
    UTXOIndexUInt32ArrayCompressed TableUInt32Array = new UTXOIndexUInt32ArrayCompressed();

    static string PathUTXOState = "UTXOArchive";
    static string PathUTXOStateOld = PathUTXOState + "_Old";

    public int BlockHeight;
    Header Header;

    long UTCTimeStartMerger;

    public UTXOSynchronizer Synchronizer;
    public Network Network;



    public UTXOTable(
      byte[] genesisBlockBytes,
      Headerchain headerchain)
    {
      Headerchain = headerchain;

      Tables = new UTXOIndexCompressed[]{
          TableUInt32,
          TableULong64,
          TableUInt32Array };

      GenesisBlockBytes = genesisBlockBytes;

      Synchronizer = new UTXOSynchronizer(this);
    }



    public async Task Start()
    {
      await Synchronizer.Start();
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
    
    void SpendUTXOs(List<TXInput> inputs)
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

        throw new UTXOException(string.Format(
          "Referenced TX {0} not found in UTXO table.",
          inputs[i].TXIDOutput.ToHexString()));
      }
    }


       
    void LoadImage(out int archiveIndex)
    {
      if (TryLoadUTXOState(out archiveIndex))
      {
        Console.WriteLine("Load UTXO Image from {0}", 
          PathUTXOState);
        return;
      }

      if (Directory.Exists(PathUTXOState))
      {
        Directory.Delete(PathUTXOState, true);
      }

      if (Directory.Exists(PathUTXOStateOld))
      {
        Directory.Move(PathUTXOStateOld, PathUTXOState);

        if (TryLoadUTXOState(out archiveIndex))
        {
          Console.WriteLine("Load UTXO Image from {0}",
            PathUTXOStateOld);
          return;
        }

        Directory.Delete(PathUTXOState, true);
      }

      Console.WriteLine("no image loaded, build from genesis",
        PathUTXOState,
        PathUTXOStateOld);

      BlockContainer genesisBlockContainer = new BlockContainer(
        Headerchain,
        0,
        GenesisBlockBytes);

      genesisBlockContainer.TryParse();
      
      InsertContainer(genesisBlockContainer);
    }



    bool TryLoadUTXOState(out int archiveIndex)
    {
      try
      {
        byte[] uTXOState = File.ReadAllBytes(
          Path.Combine(PathUTXOState, "UTXOState"));

        archiveIndex = BitConverter.ToInt32(uTXOState, 0);
        BlockHeight = BitConverter.ToInt32(uTXOState, 4);

        byte[] headerHashMergedLast = new byte[HASH_BYTE_SIZE];
        Array.Copy(uTXOState, 8, headerHashMergedLast, 0, HASH_BYTE_SIZE);
        Header = Headerchain.ReadHeader(headerHashMergedLast);

        LoadMapBlockToArchiveData(File.ReadAllBytes(
          Path.Combine(PathUTXOState, "MapBlockHeader")));

        for (int c = 0; c < Tables.Length; c += 1)
        {
          Tables[c].Load();
        }

        return true;
      }
      catch
      {
        archiveIndex = 0;
        BlockHeight = -1;
        Header = null;

        for (int c = 0; c < Tables.Length; c += 1)
        {
          Tables[c].Clear();
        }

        return false;
      }
    }

    // Similar function as LoadCollisionData in UTXOIndexUInt32Compressed
    void LoadMapBlockToArchiveData(byte[] buffer)
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



    void InsertContainer(BlockContainer container)
    {
      container.StopwatchMerging.Start();

      InsertUTXOsUInt32(
        container.UTXOsUInt32,
        container.Index);

      InsertUTXOsULong64(
        container.UTXOsULong64,
        container.Index);

      InsertUTXOsUInt32Array(
        container.UTXOsUInt32Array,
        container.Index);

      SpendUTXOs(container.Inputs);

      container.StopwatchMerging.Stop();

      Header = container.Header;
      BlockHeight += container.BlockCount;
    }

    
    
    public void UnLoadBatch(DataBatch uTXOBatch)
    {
      throw new NotImplementedException();
    }

    public void BackupToDisk()
    {
      Parallel.ForEach(Tables, t =>
      {
        t.BackupToDisk(PathUTXOState);
      });
    }

       
    void LogInsertion(BlockContainer container)
    {
      if (UTCTimeStartMerger == 0)
      {
        UTCTimeStartMerger = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
      }
      
      int ratioMergeToParse =
        (int)((float)container.StopwatchMerging.ElapsedTicks * 100
        / container.StopwatchParse.ElapsedTicks);

      int countOutputs = 
        container.UTXOsUInt32.Length +
        container.UTXOsULong64.Length +
        container.UTXOsUInt32Array.Length;

      string logCSV = string.Format(
        "{0},{1},{2},{3},{4},{5},{6},{7},{8},{9}",
        container.Index,
        BlockHeight,
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
