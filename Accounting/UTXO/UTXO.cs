using System.Diagnostics;

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Threading.Tasks.Dataflow;

using BToken.Chaining;
using BToken.Networking;

namespace BToken.Accounting
{
  public partial class UTXO
  {
    Headerchain Headerchain;
    Network Network;

    protected static string RootPath = "UTXO";
    const int TABLE_ARCHIVING_INTERVAL = 200;

    const int HASH_BYTE_SIZE = 32;

    const int COUNT_BATCHINDEX_BITS = 16;
    const int COUNT_HEADER_BITS = 4;
    const int COUNT_COLLISION_BITS_PER_TABLE = 2;

    UTXOTable[] Tables = new UTXOTable[]{
        new UTXOTableUInt32(),
        new UTXOTableULong64(),
        new UTXOTableUInt32Array()};

    const int COUNT_COLLISIONS_MAX = 3;

    static readonly int CountNonOutputBits =
      COUNT_BATCHINDEX_BITS +
      COUNT_HEADER_BITS +
      COUNT_COLLISION_BITS_PER_TABLE * 3;

    long UTCTimeStartBuild;

    BufferBlock<UTXOBatch> BatchQueue = new BufferBlock<UTXOBatch>();
    readonly object BatchQueueLOCK = new object();
    List<UTXOBatch> BatchesQueued = new List<UTXOBatch>();
    const int COUNT_BATCHES_PARALLEL = 8;
    const int BATCHQUEUE_MAX_COUNT = 8;
    Dictionary<int, UTXOBatch> QueueBatchsMerge = new Dictionary<int, UTXOBatch>();
    readonly object LOCK_IndexLoad = new object();
    int IndexLoad;
    readonly object LOCK_IndexMerge = new object();
    int IndexMerge;
    int BlockHeight;
    StreamWriter BuildWriter;

    const int COUNT_BLOCK_DOWNLOAD_BATCH = 10;
    const int COUNT_DOWNLOAD_TASKS = 8;

    List<Block> BlocksPartitioned = new List<Block>();
    int CountTXsPartitioned = 0;
    int FilePartitionIndex = 0;
    const int MAX_COUNT_TXS_IN_PARTITION = 100000;

    Headerchain.ChainHeader ChainHeader;


    public UTXO(Headerchain headerchain, Network network)
    {
      Headerchain = headerchain;
      ChainHeader = Headerchain.GenesisHeader;
      Network = network;
    }

    public async Task StartAsync()
    {
      try
      {
        await LoadBatchHeight();

        await Task.WhenAll(Tables
          .Select(c => { return c.LoadAsync(); }));
      }
      catch
      {
        for (int c = 0; c < Tables.Length; c += 1)
        {
          Tables[c].Clear();
        }

        IndexMerge = 0;
        BlockHeight = -1;
      }

      await BuildAsync();
    }

    async Task LoadBatchHeight()
    {
      byte[] batchHeight = await LoadFileAsync(Path.Combine(RootPath, "BatchHeight"));
      IndexMerge = BitConverter.ToInt32(batchHeight, 0) + 1;
      IndexLoad = IndexMerge;
      BlockHeight = BitConverter.ToInt32(batchHeight, 4);
    }
    async Task BuildAsync()
    {
      DirectoryInfo directoryInfo = Directory.CreateDirectory("UTXOBuild");
      string filePatch = Path.Combine(
        directoryInfo.FullName,
        "UTXOBuild-" + DateTime.Now.ToString("yyyyddM-HHmmss") + ".csv");

      using (StreamWriter buildWriter = new StreamWriter(
        new FileStream(
          filePatch,
          FileMode.Append,
          FileAccess.Write,
          FileShare.Read)))
      {
        BuildWriter = buildWriter;

        string labelsCSV = string.Format(
          "BatchIndex," +
          "Block height," +
          "Time," +
          "Time merge," +
          "Ratio," +
          Tables[0].GetLabelsMetricsCSV() + "," +
          Tables[1].GetLabelsMetricsCSV() + "," +
          Tables[2].GetLabelsMetricsCSV());

        Console.WriteLine(labelsCSV);
        BuildWriter.WriteLine(labelsCSV);

        UTCTimeStartBuild = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

        Task[] archiveLoaderTasks = new Task[COUNT_BATCHES_PARALLEL];
        Parallel.For(0, COUNT_BATCHES_PARALLEL, i =>
        {
          archiveLoaderTasks[i] = LoadBatchesFromArchiveAsync(i);
        });

        await Task.WhenAll(archiveLoaderTasks);

        await LoadBatchesFromNetworkAsync();
      }
    }

    async Task LoadBatchesFromArchiveAsync(int loaderID)
    {
      int batchIndex;

      try
      {
        while (true)
        {
          lock (LOCK_IndexLoad)
          {
            batchIndex = IndexLoad++;
          }

          if (!BlockArchiver.Exists(batchIndex, out string filePath))
          {
            return;
          }

          UTXOBatch batch = new UTXOBatch()
          {
            BatchIndex = batchIndex,
            Buffer = await BlockArchiver.ReadBlockBatchAsync(filePath).ConfigureAwait(false)
          };

          batch.StopwatchParse.Start();
          ParseBatch(batch);
          batch.StopwatchParse.Stop();

          MergeBatch(batch);
        }
      }
      catch (Exception ex)
      {
        Console.WriteLine(ex.Message);
        throw ex;
      }
    }
    async Task LoadBatchesFromNetworkAsync()
    {
      Headerchain.ChainHeader chainHeader = ChainHeader;
      int batchIndex = IndexMerge;
      FilePartitionIndex = IndexMerge;
      var blockDownloadTasks = new List<Task>(COUNT_DOWNLOAD_TASKS);

      while (TryGetHeaderHashes(ref chainHeader, out byte[][] headerHashes))
      {
        var sessionBlockDownload = new SessionBlockDownload(
          this,
          headerHashes,
          batchIndex);

        Task blockDownloadTask = Network.ExecuteSessionAsync(sessionBlockDownload);
        blockDownloadTasks.Add(blockDownloadTask);

        if (blockDownloadTasks.Count > COUNT_DOWNLOAD_TASKS)
        {
          Task blockDownloadTaskCompleted = await Task.WhenAny(blockDownloadTasks);
          blockDownloadTasks.Remove(blockDownloadTaskCompleted);
        }

        batchIndex += 1;
      }

      await Task.WhenAll(blockDownloadTasks);
    }

    void MergeBatch(UTXOBatch batch)
    {
      lock (LOCK_IndexMerge)
      {
        if (IndexMerge != batch.BatchIndex)
        {
          QueueBatchsMerge.Add(batch.BatchIndex, batch);
          return;
        }
      }

      while (true)
      {
        batch.StopwatchMerging.Start();
        InsertUTXOs(batch);
        SpendUTXOs(batch);
        batch.StopwatchMerging.Stop();

        if (IndexMerge % TABLE_ARCHIVING_INTERVAL == 0
          && IndexMerge > 0)
        {
          BackupToDisk();
        }

        BatchReporting(batch);

        lock (LOCK_IndexMerge)
        {
          IndexMerge += 1;
          ChainHeader = batch.ChainHeader;

          if (QueueBatchsMerge.TryGetValue(IndexMerge, out batch))
          {
            QueueBatchsMerge.Remove(IndexMerge);
            continue;
          }

          break;
        }
      }
    }
    void InsertUTXOs(UTXOBatch batch)
    {
      foreach(Block block in batch.Blocks)
      {
        for (int c = 0; c < Tables.Length; c += 1)
        {
        LoopUTXOItems:
          while (block.TryPopUTXOItem(c, out UTXOItem uTXOItem))
          {
            for (int cc = 0; cc < Tables.Length; cc += 1)
            {
              if (Tables[cc].PrimaryTableContainsKey(uTXOItem.PrimaryKey))
              {
                Tables[cc].IncrementCollisionBits(uTXOItem.PrimaryKey, c);

                Tables[c].SecondaryTableAddUTXO(uTXOItem);
                goto LoopUTXOItems;
              }
            }
            Tables[c].PrimaryTableAddUTXO(uTXOItem);
          }
        }
      }
    }
    void SpendUTXOs(UTXOBatch batch)
    {
      foreach (Block block in batch.Blocks)
      {
        for(int t = 0; t < block.TXCount; t += 1)
        {
          int i = 0;
        LoopSpendUTXOs:
          while (i < block.InputsPerTX[t].Length)
          {
            TXInput input = block.InputsPerTX[t][i];
                        
            for (int c = 0; c < Tables.Length; c += 1)
            {
              UTXOTable tablePrimary = Tables[c];

              if (tablePrimary.TryGetValueInPrimaryTable(input.PrimaryKeyTXIDOutput))
              {
                UTXOTable tableCollision = null;
                for (int cc = 0; cc < Tables.Length; cc += 1)
                {
                  if (tablePrimary.HasCollision(cc))
                  {
                    tableCollision = Tables[cc];

                    if (tableCollision.TrySpendCollision(input, tablePrimary))
                    {
                      i += 1;
                      goto LoopSpendUTXOs;
                    }
                  }
                }

                tablePrimary.SpendPrimaryUTXO(input, out bool allOutputsSpent);

                if (allOutputsSpent)
                {
                  tablePrimary.RemovePrimary();

                  if (tableCollision != null)
                  {
                    batch.StopwatchResolver.Start();
                    tableCollision.ResolveCollision(tablePrimary);
                    batch.StopwatchResolver.Stop();
                  }
                }

                i += 1;
                goto LoopSpendUTXOs;
              }
            }

            throw new UTXOException(string.Format(
              "Referenced TX {0} not found in UTXO table.",
              input.TXIDOutput.ToHexString()));
          }
        }
      }
    }

    static async Task<byte[]> LoadFileAsync(string fileName)
    {
      using (FileStream fileStream = new FileStream(
        fileName,
        FileMode.Open,
        FileAccess.Read,
        FileShare.Read,
        bufferSize: 4096,
        useAsync: true))
      {
        return await ReadBytesAsync(fileStream);
      }
    }
    static async Task<byte[]> ReadBytesAsync(Stream stream)
    {
      var buffer = new byte[stream.Length];

      int bytesToRead = buffer.Length;
      int offset = 0;
      while (bytesToRead > 0)
      {
        int chunkSize = await stream.ReadAsync(buffer, offset, bytesToRead);

        offset += chunkSize;
        bytesToRead -= chunkSize;
      }

      return buffer;
    }
    static async Task WriteFileAsync(string filePath, byte[] buffer)
    {
      string filePathTemp = filePath + "_temp";

      using (FileStream stream = new FileStream(
         filePathTemp,
         FileMode.Create,
         FileAccess.ReadWrite,
         FileShare.Read,
         bufferSize: 4096,
         useAsync: true))
      {
        await stream.WriteAsync(buffer, 0, buffer.Length).ConfigureAwait(false);
      }

      if (File.Exists(filePath))
      {
        File.Delete(filePath);
      }
      File.Move(filePathTemp, filePath);
    }
    void BackupToDisk()
    {
      Directory.CreateDirectory(RootPath);

      byte[] heights = new byte[8];
      BitConverter.GetBytes(IndexMerge).CopyTo(heights, 0);
      BitConverter.GetBytes(BlockHeight).CopyTo(heights, 4);

      Task backupBlockHeightTask = WriteFileAsync(
        Path.Combine(RootPath, "BatchHeight"),
        heights);

      Parallel.ForEach(Tables, c => c.BackupToDisk());
    }
    void BatchReporting(UTXOBatch batch)
    {
      batch.SignalBatchCompletion.SetResult(batch);

      BlockHeight += batch.Blocks.Count;

      long timeParsing = batch.StopwatchParse.ElapsedMilliseconds;
      int ratioMergeToParse = (int)((float)batch.StopwatchMerging.ElapsedMilliseconds * 100 / timeParsing);
      int ratioResolveToMerge = (int)((float)batch.StopwatchResolver.ElapsedTicks * 100 / batch.StopwatchMerging.ElapsedTicks);

      string metricsCSV = string.Format(
        "{0},{1},{2},{3},{4},{5},{6},{7},{8}",
        batch.BatchIndex,
        BlockHeight,
        DateTimeOffset.UtcNow.ToUnixTimeSeconds() - UTCTimeStartBuild,
        timeParsing,
        ratioMergeToParse,
        ratioResolveToMerge,
        Tables[0].GetMetricsCSV(),
        Tables[1].GetMetricsCSV(),
        Tables[2].GetMetricsCSV());

      Console.WriteLine(metricsCSV);
      BuildWriter.WriteLine(metricsCSV);
    }
        
    void ArchiveBatch(int batchIndex, List<Block> blocks)
    {
      //BlocksPartitioned.AddRange(blocks);
      //CountTXsPartitioned += blocks.Sum(b => b.TXs.Length);

      //if (CountTXsPartitioned > MAX_COUNT_TXS_IN_PARTITION)
      //{
      //  Task archiveBlocksTask = BlockArchiver.ArchiveBlocksAsync(
      //    BlocksPartitioned,
      //    FilePartitionIndex);

      //  FilePartitionIndex += 1;

      //  BlocksPartitioned = new List<Block>();
      //  CountTXsPartitioned = 0;
      //}
    }

    static bool TryGetHeaderHashes(
      ref Headerchain.ChainHeader chainHeader,
      out byte[][] headerHashes)
    {
      if (chainHeader == null)
      {
        headerHashes = null;
        return false;
      }

      headerHashes = new byte[COUNT_BLOCK_DOWNLOAD_BATCH][];

      for (int i = 0; i < headerHashes.Length && chainHeader.HeadersNext != null; i += 1)
      {
        headerHashes[i] = chainHeader.GetHeaderHash();
        chainHeader = chainHeader.HeadersNext[0];
      }

      return true;
    }

    
  }
}