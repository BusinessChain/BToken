using System.Diagnostics;

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Threading.Tasks.Dataflow;
using System.Security.Cryptography;
using System.Runtime.Remoting.Metadata.W3cXsd2001;

using BToken.Chaining;
using BToken.Networking;

namespace BToken.Accounting
{
  public partial class UTXO
  {
    Headerchain Headerchain;
    Network Network;

    protected static string RootPath = "UTXO";

    const int CACHE_ARCHIVING_INTERVAL = 100;
    UTXOTable[] Tables;

    const int BYTE_LENGTH_VERSION = 4;
    const int BYTE_LENGTH_OUTPUT_VALUE = 8;
    const int BYTE_LENGTH_LOCK_TIME = 4;


    const int COUNT_HEADER_BYTES = 80;
    const int OFFSET_INDEX_HASH_PREVIOUS = 4;
    const int OFFSET_INDEX_MERKLE_ROOT = 36;
    const int HASH_BYTE_SIZE = 32;
    const int TWICE_HASH_BYTE_SIZE = HASH_BYTE_SIZE << 1;

    const int COUNT_HEADERINDEX_BITS = 26;
    const int COUNT_COLLISION_BITS = 3;

    static readonly int CountHeaderBytes = (COUNT_HEADERINDEX_BITS + 7) / 8;
    static readonly int CountHeaderPlusCollisionBits = COUNT_HEADERINDEX_BITS + COUNT_COLLISION_BITS;

    long UTCTimeStartup;
    Stopwatch Stopwatch = new Stopwatch();

    BufferBlock<UTXOBatch> BatchQueue = new BufferBlock<UTXOBatch>();
    readonly object BatchQueueLOCK = new object();
    List<UTXOBatch> BatchesQueued = new List<UTXOBatch>();
    const int COUNT_BATCHES_PARALLEL = 8;
    const int BATCHQUEUE_MAX_COUNT = 8;
    Dictionary<int, UTXOBatch> QueueBatchsMerge = new Dictionary<int, UTXOBatch>();
    readonly object MergeLOCK = new object();
    int IndexBatchMerge;
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

      Tables = new UTXOTable[]{
        new UTXOTableUInt32(),
        new UTXOTableULong64(),
        new UTXOTableByteArray()};
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

        IndexBatchMerge = 0;
        BlockHeight = -1;
      }

      await BuildAsync();
    }

    async Task LoadBatchHeight()
    {
      byte[] batchHeight = await LoadFileAsync(Path.Combine(RootPath, "BatchHeight"));
      IndexBatchMerge = BitConverter.ToInt32(batchHeight, 0) + 1;
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

        UTCTimeStartup = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

        Parallel.For(0, COUNT_BATCHES_PARALLEL, i =>
        {
          Task mergeBatchTask = MergeBatchesQueuedAsync(i);
        });

        await LoadBatchesFromArchiveAsync();
        await LoadBatchesFromNetworkAsync();
      }
    }

    async Task LoadBatchesFromArchiveAsync()
    {
      try
      {
        int batchIndex = IndexBatchMerge;

        while (BlockArchiver.Exists(batchIndex, out string filePath))
        {
          UTXOBatch batch = new UTXOBatch()
          {
            BatchIndex = batchIndex,
            Buffer = await BlockArchiver.ReadBlockBatchAsync(filePath).ConfigureAwait(false)
          };

          BatchesQueued.Add(batch);
          BatchQueue.Post(batch);

          if (BatchesQueued.Count > BATCHQUEUE_MAX_COUNT)
          {
            BatchesQueued.Remove(await await Task.WhenAny(BatchesQueued
              .Select(b => b.SignalBatchCompletion.Task)));
          }

          batchIndex += 1;
        }

        await Task.WhenAll(BatchesQueued.Select(b => b.SignalBatchCompletion.Task));
      }
      catch (Exception ex)
      {
        Console.WriteLine(ex.Message);
      }

    }

    async Task MergeBatchesQueuedAsync(int mergerID)
    {
      while (true)
      {
        UTXOBatch batch = await BatchQueue.ReceiveAsync().ConfigureAwait(false);

        batch.StopwatchParse.Start();

        int bufferIndex = 0;
        while (bufferIndex < batch.Buffer.Length)
        {
          try
          {
            ParseBlock(batch, ref bufferIndex);
          }
          catch (Exception ex)
          {
            Console.WriteLine(ex.Message);
          }
        }

        batch.StopwatchParse.Stop();

        lock (MergeLOCK)
        {
          if (IndexBatchMerge != batch.BatchIndex)
          {
            QueueBatchsMerge.Add(batch.BatchIndex, batch);
            continue;
          }
        }

        while (true)
        {
          batch.StopwatchMerging.Start();
          try
          {
            if (batch.BatchIndex == 164)
            { }

            InsertUTXOs(batch);
            SpendUTXOs(batch);
          }
          catch (Exception ex)
          {
            Console.WriteLine(ex.Message);
          }
          batch.StopwatchMerging.Stop();

          if (IndexBatchMerge % CACHE_ARCHIVING_INTERVAL == 0 && IndexBatchMerge > 0)
          {
            //BackupToDisk();
          }

          BatchReporting(batch, mergerID);

          lock (MergeLOCK)
          {
            IndexBatchMerge += 1;
            ChainHeader = batch.ChainHeader;

            if (QueueBatchsMerge.TryGetValue(IndexBatchMerge, out batch))
            {
              QueueBatchsMerge.Remove(IndexBatchMerge);
              continue;
            }

            break;
          }
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
              if (Tables[cc].TrySetCollisionBit(uTXOItem.PrimaryKey, c))
              {
                Tables[c].SecondaryCacheAddUTXO(uTXOItem);
                goto LoopUTXOItems;
              }
            }

            Tables[c].PrimaryCacheAddUTXO(uTXOItem);
          }
        }
      }
    }
    void SpendUTXOs(UTXOBatch batch)
    {
      foreach (Block block in batch.Blocks)
      {
        if (block.HeaderHashString == "000000000000068E8BC892FD269A8C12B89CB3DEA9990745416F762EA42A14EB")
        { Console.WriteLine(block.HeaderHashString); }


        for(int t = 0; t < block.TXCount; t += 1)
        {
          if (t == 50)
          { }

          int i = 0;
        LoopSpendUTXOs:
          while (i < block.InputsPerTX[t].Length)
          {
            TXInput input = block.InputsPerTX[t][i];

            if (input.TXIDOutput.ToHexString() == "CE72EE3DF7B0D60016661476679AE884BFF0D5054535827DF42D028570CDDB9F")
            { }
            
            for (int c = 0; c < Tables.Length; c += 1)
            {
              UTXOTable table = Tables[c];

              if (table.TryGetValueInPrimaryCache(input.PrimaryKeyTXIDOutput))
              {
                UTXOTable cacheCollision = null;
                uint collisionBits = 0;
                while (c < Tables.Length)
                {
                  if (table.IsCollision(c))
                  {
                    cacheCollision = Tables[c];

                    collisionBits |= (uint)(1 << c);

                    if (cacheCollision.TrySpendSecondary(
                      input,
                      table))
                    {
                      i += 1;
                      goto LoopSpendUTXOs;
                    }
                  }

                  c += 1;
                }

                table.SpendPrimaryUTXO(input, out bool areAllOutputsSpent);

                if (areAllOutputsSpent)
                {
                  table.RemovePrimary(input.PrimaryKeyTXIDOutput);

                  if (cacheCollision != null)
                  {
                    cacheCollision.ResolveCollision(input.PrimaryKeyTXIDOutput, collisionBits);
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
        await stream.WriteAsync(buffer, 0, buffer.Length);
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
      BitConverter.GetBytes(IndexBatchMerge).CopyTo(heights, 0);
      BitConverter.GetBytes(BlockHeight).CopyTo(heights, 4);

      Task backupBlockHeightTask = WriteFileAsync(
        Path.Combine(RootPath, "BatchHeight"),
        heights);

      Parallel.ForEach(Tables, c => c.BackupToDisk());
    }
    void BatchReporting(UTXOBatch batch, int mergerID)
    {
      batch.SignalBatchCompletion.SetResult(batch);

      BlockHeight += batch.Blocks.Count;
      
      long timeParsePlusMerge = batch.StopwatchMerging.ElapsedMilliseconds + batch.StopwatchParse.ElapsedMilliseconds;
      int ratio = (int)((float)batch.StopwatchMerging.ElapsedTicks * 100 / batch.StopwatchParse.ElapsedTicks);

      string metricsCSV = string.Format("{0},{1},{2},{3},{4},{5},{6},{7}",
        batch.BatchIndex,
        BlockHeight,
        DateTimeOffset.UtcNow.ToUnixTimeSeconds() - UTCTimeStartup,
        timeParsePlusMerge,
        ratio,
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

    async Task LoadBatchesFromNetworkAsync()
    {
      Headerchain.ChainHeader chainHeader = ChainHeader;
      int indexBatchDownload = IndexBatchMerge;
      FilePartitionIndex = IndexBatchMerge;
      var blockDownloadTasks = new List<Task>(COUNT_DOWNLOAD_TASKS);

      while (TryGetHeaderHashes(ref chainHeader, out byte[][] headerHashes))
      {
        var sessionBlockDownload = new SessionBlockDownload(
          this,
          headerHashes,
          indexBatchDownload);

        Task blockDownloadTask = Network.ExecuteSessionAsync(sessionBlockDownload);
        blockDownloadTasks.Add(blockDownloadTask);

        if (blockDownloadTasks.Count > COUNT_DOWNLOAD_TASKS)
        {
          Task blockDownloadTaskCompleted = await Task.WhenAny(blockDownloadTasks);
          blockDownloadTasks.Remove(blockDownloadTaskCompleted);
        }

        indexBatchDownload += 1;
      }

      await Task.WhenAll(blockDownloadTasks);
    }
    
  }
}