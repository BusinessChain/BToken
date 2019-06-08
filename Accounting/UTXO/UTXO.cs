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
    const int UTXOSTATE_ARCHIVING_INTERVAL = 50;

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
    const int COUNT_BATCHES_PARALLEL = 1;
    const int BATCHQUEUE_MAX_COUNT = 16;
    Dictionary<int, UTXOBatch> QueueBatchsMerge = new Dictionary<int, UTXOBatch>();
    readonly object LOCK_IndexFilePartition = new object();
    readonly object LOCK_BatchIndexMerge = new object();
    int BatchIndexMerge = 0;
    int BlockHeight = 0;
    StreamWriter BuildWriter;

    const int COUNT_BLOCK_DOWNLOAD_BATCH = 10;
    const int COUNT_DOWNLOAD_TASKS = 8;

    List<Block> BlocksPartitioned = new List<Block>();
    int CountTXsPartitioned = 0;
    int FilePartitionIndex = 0;
    const int MAX_COUNT_TXS_IN_PARTITION = 100;

    Headerchain.ChainHeader ChainHeaderMergedLast;
    BitcoinGenesisBlock GenesisBlock;


    public UTXO(BitcoinGenesisBlock genesisBlock, Headerchain headerchain, Network network)
    {
      GenesisBlock = genesisBlock;
      Headerchain = headerchain;
      Network = network;
    }

    public async Task StartAsync()
    {
      try
      {
        await LoadUTXOState();
        await Task.WhenAll(Tables.Select(c => { return c.LoadAsync(); }));
      }
      catch
      {
        for (int c = 0; c < Tables.Length; c += 1)
        {
          Tables[c].Clear();
        }

        InsertGenesisBlock();

        ChainHeaderMergedLast = Headerchain.GenesisHeader;
      }

      await BuildAsync();
    }
    void InsertGenesisBlock()
    {
      UTXOBatch genesisBatch = new UTXOBatch()
      {
        BatchIndex = 0,
        Buffer = GenesisBlock.BlockBytes
      };

      ParseBatch(genesisBatch);

      InsertUTXOs(genesisBatch);
    }

    async Task LoadUTXOState()
    {
      byte[] uTXOState = await LoadFileAsync(Path.Combine(RootPath, "UTXOState"));
      BatchIndexMerge = BitConverter.ToInt32(uTXOState, 0) + 1;
      FilePartitionIndex = BatchIndexMerge;
      BlockHeight = BitConverter.ToInt32(uTXOState, 4);

      byte[] chainHeaderHash = new byte[HASH_BYTE_SIZE];
      Array.Copy(uTXOState, 8, chainHeaderHash, 0, HASH_BYTE_SIZE);
      ChainHeaderMergedLast = Headerchain.ReadHeader(chainHeaderHash);
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
          lock (LOCK_IndexFilePartition)
          {
            batchIndex = FilePartitionIndex;
            FilePartitionIndex += 1;
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

          await MergeBatchAsync(
            batch, 
            enableBlockArchiving: false);
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
      int batchIndex = BatchIndexMerge;
      FilePartitionIndex = BatchIndexMerge;
      var blockDownloadTasks = new List<Task>(COUNT_DOWNLOAD_TASKS);
      Headerchain.ChainHeader chainHeaderFetchHashes = ChainHeaderMergedLast.HeadersNext[0];

      while (TryGetHeaderHashes(out byte[][] headerHashes, ref chainHeaderFetchHashes))
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

    bool TryGetHeaderHashes(
      out byte[][] headerHashes, 
      ref Headerchain.ChainHeader chainHeaderFetchHashes)
    {
      headerHashes = new byte[COUNT_BLOCK_DOWNLOAD_BATCH][];

      for (
        int i = 0;
        i < headerHashes.Length && chainHeaderFetchHashes.HeadersNext != null;
        i += 1)
      {
        headerHashes[i] = chainHeaderFetchHashes.GetHeaderHash();
        chainHeaderFetchHashes = chainHeaderFetchHashes.HeadersNext[0];
      }

      return true;
    }

    async Task MergeBatchAsync(
      UTXOBatch batch, 
      bool enableBlockArchiving)
    {
      bool isBatchQueued = false;

      lock (LOCK_BatchIndexMerge)
      {
        if (BatchIndexMerge != batch.BatchIndex)
        {
          isBatchQueued = true;
          QueueBatchsMerge.Add(batch.BatchIndex, batch);
        }
      }

      if(isBatchQueued)
      {
        while(QueueBatchsMerge.Count > BATCHQUEUE_MAX_COUNT)
        {
          await Task.Delay(500);
        }

        return;
      }

      while (true)
      {
        if (!batch.HeaderHashPrevious.IsEqual(ChainHeaderMergedLast.GetHeaderHash())
          && batch.BatchIndex > 0)
        {
          throw new UTXOException(
            string.Format("Batch {0} with Hash previous \n{1} " +
            "does not link to last block merged \n{2}.",
            batch.BatchIndex,
            batch.HeaderHashPrevious.ToHexString(),
            ChainHeaderMergedLast.GetHeaderHash().ToHexString()));
        }

        batch.StopwatchMerging.Start();
        InsertUTXOs(batch);
        SpendUTXOs(batch);
        batch.StopwatchMerging.Stop();

        ChainHeaderMergedLast = batch.ChainHeader.HeaderPrevious;
        BlockHeight += batch.Blocks.Count;

        if (batch.BatchIndex % UTXOSTATE_ARCHIVING_INTERVAL == 0
          && batch.BatchIndex > 0)
        {
          ArchiveUTXOState(BlockHeight, batch);
        }

        if (enableBlockArchiving)
        {
          ArchiveBlocks(batch.Blocks);
        }

        BatchReporting(batch);

        lock (LOCK_BatchIndexMerge)
        {
          BatchIndexMerge += 1;

          if (QueueBatchsMerge.TryGetValue(BatchIndexMerge, out batch))
          {
            QueueBatchsMerge.Remove(BatchIndexMerge);
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

    void ArchiveUTXOState(int blockHeight, UTXOBatch batch)
    {
      Directory.CreateDirectory(RootPath);

      byte[] uTXOState = new byte[40];
      BitConverter.GetBytes(FilePartitionIndex).CopyTo(uTXOState, 0);
      BitConverter.GetBytes(blockHeight).CopyTo(uTXOState, 4);
      batch.ChainHeader.NetworkHeader.HashPrevious.CopyTo(uTXOState, 8);

      Task writeFileTask = WriteFileAsync(
        Path.Combine(RootPath, "UTXOState"), 
        uTXOState);

      Parallel.ForEach(Tables, c => c.BackupToDisk());
    }
    void ArchiveBlocks(List<Block> blocks)
    {
      BlocksPartitioned.AddRange(blocks);
      CountTXsPartitioned += blocks.Sum(b => b.TXCount);

      if (CountTXsPartitioned > MAX_COUNT_TXS_IN_PARTITION)
      {
        Task archiveBlocksTask = BlockArchiver.ArchiveBlocksAsync(
          BlocksPartitioned,
          FilePartitionIndex);

        FilePartitionIndex += 1;

        BlocksPartitioned = new List<Block>();
        CountTXsPartitioned = 0;
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
      try
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
      catch(Exception ex)
      {
        Console.WriteLine(ex.Message);
      }
    }
    void BatchReporting(UTXOBatch batch)
    {
      long timeParsing = batch.StopwatchParse.ElapsedMilliseconds;

      int ratioMergeToParse = 
        (int)((float)batch.StopwatchMerging.ElapsedTicks * 100 
        / batch.StopwatchParse.ElapsedTicks);
      
      string metricsCSV = string.Format(
        "{0},{1},{2},{3},{4},{5},{6},{7}",
        batch.BatchIndex,
        BlockHeight,
        DateTimeOffset.UtcNow.ToUnixTimeSeconds() - UTCTimeStartBuild,
        timeParsing,
        ratioMergeToParse,
        Tables[0].GetMetricsCSV(),
        Tables[1].GetMetricsCSV(),
        Tables[2].GetMetricsCSV());

      Console.WriteLine(metricsCSV);
      BuildWriter.WriteLine(metricsCSV);
    }
  }
}