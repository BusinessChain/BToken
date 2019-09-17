﻿using System;
using System.Collections.Generic;
using System.IO;
using System.Diagnostics;
using System.Threading.Tasks;
using System.Threading.Tasks.Dataflow;

namespace BToken.Chaining
{
  public partial class Blockchain
  {
    partial class UTXOTable : IDatabase
    {
      Blockchain Blockchain;

      const int LENGTH_BITS_UINT = 32;
      const int LENGTH_BITS_ULONG = 64;

      const int COUNT_BATCHINDEX_BITS = 16;
      const int COUNT_COLLISION_BITS_PER_TABLE = 2;
      const int COUNT_COLLISIONS_MAX = 2 ^ COUNT_COLLISION_BITS_PER_TABLE - 1;
      
      static readonly int CountNonOutputBits =
        COUNT_BATCHINDEX_BITS +
        COUNT_COLLISION_BITS_PER_TABLE * 3;

      UTXOIndexCompressed[] Tables;
      UTXOIndexUInt32Compressed TableUInt32 = new UTXOIndexUInt32Compressed();
      UTXOIndexULong64Compressed TableULong64 = new UTXOIndexULong64Compressed();
      UTXOIndexUInt32ArrayCompressed TableUInt32Array = new UTXOIndexUInt32ArrayCompressed();

      const int UTXOSTATE_ARCHIVING_INTERVAL = 500;
      static string PathUTXOState = "UTXOArchive";
      static string PathUTXOStateOld = PathUTXOState + "_Old";

      public int BlockHeight;
      public int BatchIndexNext;
      int BatchIndexMergedLast;
      public Header HeaderMergedLast;
      public BufferBlock<UTXOBatch> InputBuffer = new BufferBlock<UTXOBatch>(
        new DataflowBlockOptions { BoundedCapacity = 10 });

      long UTCTimeStartMerger;
      public Stopwatch StopwatchMerging = new Stopwatch();


      public UTXOTable(Blockchain blockchain)
      {
        Blockchain = blockchain;

        Tables = new UTXOIndexCompressed[]{
          TableUInt32,
          TableULong64,
          TableUInt32Array };

        UTCTimeStartMerger = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
      }

      void InsertUTXOsUInt32(KeyValuePair<byte[], uint>[] uTXOsUInt32)
      {
        int i = 0;

      LoopUTXOItems:
        while (i < uTXOsUInt32.Length)
        {
          int primaryKey = BitConverter.ToInt32(uTXOsUInt32[i].Key, 0);

          for (int c = 0; c < Tables.Length; c += 1)
          {
            if (Tables[c].PrimaryTableContainsKey(primaryKey))
            {
              Tables[c].IncrementCollisionBits(primaryKey, 0);

              TableUInt32.CollisionTables[uTXOsUInt32[i].Key[0]].Add(uTXOsUInt32[i].Key, uTXOsUInt32[i].Value);

              i += 1;
              goto LoopUTXOItems;
            }
          }

          TableUInt32.PrimaryTables[(byte)primaryKey].Add(primaryKey, uTXOsUInt32[i].Value);

          i += 1;
        }
      }
      void InsertUTXOsULong64(KeyValuePair<byte[], ulong>[] uTXOsULong64)
      {
        int i = 0;

      LoopUTXOItems:
        while (i < uTXOsULong64.Length)
        {
          int primaryKey = BitConverter.ToInt32(uTXOsULong64[i].Key, 0);

          for (int c = 0; c < Tables.Length; c += 1)
          {
            if (Tables[c].PrimaryTableContainsKey(primaryKey))
            {
              Tables[c].IncrementCollisionBits(primaryKey, 1);

              TableULong64.CollisionTable.Add(uTXOsULong64[i].Key, uTXOsULong64[i].Value);

              i += 1;
              goto LoopUTXOItems;
            }
          }

          TableULong64.PrimaryTable.Add(primaryKey, uTXOsULong64[i].Value);

          i += 1;
        }
      }
      void InsertUTXOsUInt32Array(KeyValuePair<byte[], uint[]>[] uTXOsUInt32Array)
      {
        int i = 0;

      LoopUTXOItems:
        while (i < uTXOsUInt32Array.Length)
        {
          int primaryKey = BitConverter.ToInt32(uTXOsUInt32Array[i].Key, 0);

          for (int c = 0; c < Tables.Length; c += 1)
          {
            if (Tables[c].PrimaryTableContainsKey(primaryKey))
            {
              Tables[c].IncrementCollisionBits(primaryKey, 2);

              TableUInt32Array.CollisionTable.Add(uTXOsUInt32Array[i].Key, uTXOsUInt32Array[i].Value);

              i += 1;
              goto LoopUTXOItems;
            }
          }

          TableUInt32Array.PrimaryTable.Add(primaryKey, uTXOsUInt32Array[i].Value);

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
      
      void ArchiveState()
      {
        if (Directory.Exists(PathUTXOState))
        {
          if (Directory.Exists(PathUTXOStateOld))
          {
            Directory.Delete(PathUTXOStateOld, true);
          }
          Directory.Move(PathUTXOState, PathUTXOStateOld);
        }

        Directory.CreateDirectory(PathUTXOState);

        byte[] uTXOState = new byte[40];
        BitConverter.GetBytes(BatchIndexMergedLast).CopyTo(uTXOState, 0);
        BitConverter.GetBytes(BlockHeight).CopyTo(uTXOState, 4);
        HeaderMergedLast.HeaderHash.CopyTo(uTXOState, 8);

        using (FileStream stream = new FileStream(
           Path.Combine(PathUTXOState, "UTXOState"),
           FileMode.Create,
           FileAccess.ReadWrite,
           FileShare.Read))
        {
          stream.Write(uTXOState, 0, uTXOState.Length);
        }

        Parallel.ForEach(Tables, t =>
        {
          t.BackupToDisk(PathUTXOState);
        });
      }

      public int LoadImage()
      {
        if (Directory.Exists(PathUTXOState))
        {
          if (!TryLoadUTXOState(out int batchIndexMergedLast))
          {
            Directory.Delete(PathUTXOState, true);

            if (Directory.Exists(PathUTXOStateOld))
            {
              Directory.Move(PathUTXOStateOld, PathUTXOState);

              if (TryLoadUTXOState(out batchIndexMergedLast))
              {
                return batchIndexMergedLast;
              }

              Directory.Delete(PathUTXOState, true);
            }
          }
        }

        BlockBatchContainer genesisBlockContainer = new BlockBatchContainer(
          new BlockParser(Blockchain.Chain),
          0,
          Blockchain.GenesisBlock.BlockBytes);

        genesisBlockContainer.Parse();

        TryInsertDataContainer(genesisBlockContainer);

        return 1;
      }


      bool TryLoadUTXOState(out int batchIndexMergedLast)
      {
        try
        {
          byte[] uTXOState = File.ReadAllBytes(Path.Combine(PathUTXOState, "UTXOState"));

          BatchIndexMergedLast = BitConverter.ToInt32(uTXOState, 0);
          batchIndexMergedLast = BatchIndexMergedLast;
          BatchIndexNext = BatchIndexMergedLast + 1;
          BlockHeight = BitConverter.ToInt32(uTXOState, 4);

          byte[] headerHashMergedLast = new byte[HASH_BYTE_SIZE];
          Array.Copy(uTXOState, 8, headerHashMergedLast, 0, HASH_BYTE_SIZE);
          HeaderMergedLast = Blockchain.Chain.ReadHeader(headerHashMergedLast);

          Parallel.ForEach(Tables, t => t.Load());

          return true;
        }
        catch (Exception ex)
        {
          for (int c = 0; c < Tables.Length; c += 1)
          {
            Tables[c].Clear();
          }

          batchIndexMergedLast = 0;
          BatchIndexNext = 0;
          BlockHeight = -1;
          HeaderMergedLast = null;

          Console.WriteLine("Exception when loading UTXO state {0}", ex.Message);
          return false;
        }
      }

      public bool TryInsertDataContainer(ItemBatchContainer dataContainer)
      {
        BlockBatchContainer blockBatchContainer = (BlockBatchContainer)dataContainer;
        
        if (blockBatchContainer.HeaderPrevious != HeaderMergedLast)
        {
          Console.WriteLine("HeaderPrevious {0} of blockBatchContainer {1} not equal to \nHeaderMergedLast {2}",
            blockBatchContainer.HeaderPrevious.HeaderHash.ToHexString(),
            blockBatchContainer.Index,
            HeaderMergedLast.HeaderHash.ToHexString());

          return false;
        }

        try
        {
          InsertUTXOsUInt32(blockBatchContainer.UTXOsUInt32);
          InsertUTXOsULong64(blockBatchContainer.UTXOsULong64);
          InsertUTXOsUInt32Array(blockBatchContainer.UTXOsUInt32Array);
          SpendUTXOs(blockBatchContainer.Inputs);
        }
        catch (ChainException ex)
        {
          Console.WriteLine(
            "Insertion of headerBatchContainer {0} raised ChainException:\n {1}.",
            dataContainer.Index,
            ex.Message);

          return false;
        }

        BlockHeight += blockBatchContainer.BlockCount;
        BatchIndexMergedLast = blockBatchContainer.Index;
        HeaderMergedLast = blockBatchContainer.HeaderLast;

        if (blockBatchContainer.Index % UTXOSTATE_ARCHIVING_INTERVAL == 0
          && blockBatchContainer.Index > 0)
        {
          ArchiveState();
        }

        LogCSV(blockBatchContainer);

        return true;
      }

      public bool TryInsertBatch(DataBatch batch, out ItemBatchContainer containerInvalid)
      {
        throw new NotImplementedException();
      }
      public Task ArchiveBatchAsync(DataBatch batch)
      {
        throw new NotImplementedException();
      }



      string FilePath = "J:\\BlockArchivePartitioned\\p";

      public ItemBatchContainer LoadDataArchive(int archiveIndex)
      {
        return new BlockBatchContainer(
          new BlockParser(Blockchain.Chain),
          archiveIndex,
          File.ReadAllBytes(FilePath + archiveIndex));
      }


      void LogCSV(BlockBatchContainer blockBatchContainer)
      {
        int ratioMergeToParse =
          (int)((float)StopwatchMerging.ElapsedTicks * 100
          / blockBatchContainer.StopwatchParse.ElapsedTicks);

        string logCSV = string.Format(
          "{0},{1},{2},{3},{4},{5},{6},{7}",
          blockBatchContainer.Index,
          BlockHeight,
          DateTimeOffset.UtcNow.ToUnixTimeSeconds() - UTCTimeStartMerger,
          blockBatchContainer.StopwatchParse.ElapsedMilliseconds,
          ratioMergeToParse,
          Tables[0].GetMetricsCSV(),
          Tables[1].GetMetricsCSV(),
          Tables[2].GetMetricsCSV());

        Console.WriteLine(logCSV);
      }
    }
  }
}
