using System.Diagnostics;

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Threading.Tasks.Dataflow;
using System.Runtime.Remoting.Metadata.W3cXsd2001;
using System.Security.Cryptography;

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
    UTXOCache[] Caches;

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

    BufferBlock<BatchBlockLoad> BatchQueue = new BufferBlock<BatchBlockLoad>();
    readonly object BatchQueueLOCK = new object();
    List<BatchBlockLoad> BatchesQueued = new List<BatchBlockLoad>();
    const int COUNT_BATCHES_PARALLEL = 4;
    const int BATCHQUEUE_MAX_COUNT = 8;
    Dictionary<int, BatchBlockLoad> QueueBatchsMerge = new Dictionary<int, BatchBlockLoad>();
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

      Caches = new UTXOCache[]{
        new UTXOCacheUInt32(),
        new UTXOCacheULong64(),
        new UTXOCacheByteArray()};
    }

    public async Task StartAsync()
    {
      try
      {
        await LoadBatchHeight();

        await Task.WhenAll(Caches
          .Select(c => { return c.LoadAsync(); }));
      }
      catch
      {
        for(int c = 0; c < Caches.Length; c += 1)
        {
          Caches[c].Clear();
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
          Caches[0].GetLabelsMetricsCSV() + "," +
          Caches[1].GetLabelsMetricsCSV() + "," +
          Caches[2].GetLabelsMetricsCSV());

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
          BatchBlockLoad batch = new BatchBlockLoad()
          {
            Index = batchIndex,
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
        BatchBlockLoad batch = await BatchQueue.ReceiveAsync().ConfigureAwait(false);

        batch.StopwatchParse.Start();

        int bufferIndex = 0;
        while (bufferIndex < batch.Buffer.Length)
        {
          ParseBlock(batch, ref bufferIndex);
        }

        batch.StopwatchParse.Stop();

        lock (MergeLOCK)
        {
          if (IndexBatchMerge != batch.Index)
          {
            QueueBatchsMerge.Add(batch.Index, batch);

            continue;
          }
        }

        while (true)
        {
          batch.StopwatchMerging.Start();
          Merge(batch.Blocks);
          batch.StopwatchMerging.Stop();

          if (IndexBatchMerge % CACHE_ARCHIVING_INTERVAL == 0 && IndexBatchMerge > 0)
          {
            BackupToDisk();
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
    void ParseBlock(BatchBlockLoad batch, ref int bufferIndex)
    {
      byte[] headerHash =
      batch.SHA256Generator.ComputeHash(
        batch.SHA256Generator.ComputeHash(
          batch.Buffer,
          bufferIndex,
          COUNT_HEADER_BYTES));

      ValidateHeaderHash(headerHash, batch);

      int indexMerkleRoot = bufferIndex + OFFSET_INDEX_MERKLE_ROOT;

      bufferIndex += COUNT_HEADER_BYTES;

      int tXCount = VarInt.GetInt32(batch.Buffer, ref bufferIndex);
      TX[] tXs = new TX[tXCount];

      if (tXs.Length == 1)
      {
        int indexTX = bufferIndex;

        CacheTX(batch.Buffer, ref bufferIndex);

        byte[] hash = batch.SHA256Generator.ComputeHash(
         batch.SHA256Generator.ComputeHash(
           batch.Buffer,
           indexTX,
           bufferIndex - indexTX));

        if (!hash.IsEqual(batch.Buffer, indexMerkleRoot))
        {
          throw new UTXOException(
            string.Format("Payload corrupted. batchIndex {0}, bufferIndex: {1}", 
            batch.Index,
            bufferIndex));
        }

        return;
      }
    }
    void CacheTX(byte[] buffer, ref int bufferIndex)
    {
      bufferIndex += 4; // version

      bool isWitnessFlagPresent = buffer[bufferIndex] == 0x00;
      if (isWitnessFlagPresent)
      {
        bufferIndex += 2;
      }

      int countTXInputs = VarInt.GetInt32(buffer, ref bufferIndex);
      for (int i = 0; i < countTXInputs; i += 1)
      {
        inputs[i] = TXInput.Parse(buffer, ref bufferIndex);
      }

      int tXOutputsCount = VarInt.GetInt32(buffer, ref bufferIndex);
      var outputs = new TXOutput[tXOutputsCount];
      for (int i = 0; i < tXOutputsCount; i += 1)
      {
        outputs[i] = TXOutput.Parse(buffer, ref bufferIndex);
      }

      if (isWitnessFlagPresent)
      {
        var witnesses = new TXWitness[countTXInputs];
        for (int i = 0; i < countTXInputs; i += 1)
        {
          witnesses[i] = TXWitness.Parse(buffer, ref bufferIndex);
        }
      }

      bufferIndex += 4; // Lock time
    }
    void Merge(List<Block> blocks)
    {
      foreach (Block block in blocks)
      {
        InsertUTXOs(block.TXs, block.HeaderHash);
        SpendUTXOs(block.TXs);

        BlockHeight += 1;
      }
    }
    void InsertUTXOs(TX[] tXs, byte[] headerHash)
    {
      int t = 0;

    LoopInsertUTXOs:
      while (t < tXs.Length)
      {
        for (int c = 0; c < Caches.Length; c += 1)
        {
          if (Caches[c].IsUTXOTooLongForCache(tXs[t].LengthUTXOBits))
          {
            continue;
          }

          Caches[c].CreateUTXO(headerHash, tXs[t].LengthUTXOBits);

          for (int cc = 0; cc < Caches.Length; cc += 1)
          {
            if (Caches[cc].TrySetCollisionBit(tXs[t].PrimaryKey, Caches[c].Address))
            {
              Caches[c].SecondaryCacheAddUTXO(tXs[t].Hash);

              t += 1;
              goto LoopInsertUTXOs;
            }
          }

          Caches[c].PrimaryCacheAddUTXO(tXs[t].PrimaryKey);

          t += 1;
          goto LoopInsertUTXOs;
        }

        throw new UTXOException("UTXO could not be inserted in Cache modules.");
      }
    }
    void SpendUTXOs(TX[] tXs)
    {
      for (int t = 1; t < tXs.Length; t += 1)
      {
        int i = 0;

      LoopSpendUTXOs:
        while (i < tXs[t].Inputs.Length)
        {
          TXInput tXInput = tXs[t].Inputs[i];

          int primaryKey = BitConverter.ToInt32(tXs[t].Inputs[i].TXIDOutput, 0);

          for (int c = 0; c < Caches.Length; c += 1)
          {
            if (Caches[c].TryGetValueInPrimaryCache(primaryKey))
            {
              UTXOCache cacheCollision = null;
              uint collisionBits = 0;
              for (int cc = 0; cc < Caches.Length; cc += 1)
              {
                if (Caches[c].IsCollision(Caches[cc].Address))
                {
                  cacheCollision = Caches[cc];

                  collisionBits |= (uint)(1 << cacheCollision.Address);

                  if (cacheCollision.TrySpendSecondary(
                    primaryKey,
                    tXInput.TXIDOutput,
                    tXInput.IndexOutput,
                    Caches[c]))
                  {
                    i += 1;
                    goto LoopSpendUTXOs;
                  }
                }
              }

              Caches[c].SpendPrimaryUTXO(primaryKey, tXInput.IndexOutput, out bool areAllOutputpsSpent);

              if (areAllOutputpsSpent)
              {
                Caches[c].RemovePrimary(primaryKey);

                if (cacheCollision != null)
                {
                  cacheCollision.ResolveCollision(primaryKey, collisionBits);
                }
              }

              i += 1;
              goto LoopSpendUTXOs;
            }
          }

          throw new UTXOException("Referenced TXID not found in UTXO table.");
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

      Parallel.ForEach(Caches, c => c.BackupToDisk());
    }
    void BatchReporting(BatchBlockLoad batch, int mergerID)
    {
      batch.SignalBatchCompletion.SetResult(batch);
      
      long timeParsePlusMerge = batch.StopwatchMerging.ElapsedMilliseconds + batch.StopwatchParse.ElapsedMilliseconds;
      int ratio = (int)((float)batch.StopwatchMerging.ElapsedTicks * 100 / batch.StopwatchParse.ElapsedTicks);

      string metricsCSV = string.Format("{0},{1},{2},{3},{4},{5},{6},{7}",
        batch.Index,
        BlockHeight,
        DateTimeOffset.UtcNow.ToUnixTimeSeconds() - UTCTimeStartup,
        timeParsePlusMerge,
        ratio,
        Caches[0].GetMetricsCSV(),
        Caches[1].GetMetricsCSV(),
        Caches[2].GetMetricsCSV());

      Console.WriteLine(metricsCSV);
      BuildWriter.WriteLine(metricsCSV);
    }

    
    void ArchiveBatch(int batchIndex, List<Block> blocks)
    {
      BlocksPartitioned.AddRange(blocks);
      CountTXsPartitioned += blocks.Sum(b => b.TXs.Length);

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
    
    void ValidateHeaderHash(
      byte[] headerHash,
      BatchBlockLoad batch)
    {
      if (batch.ChainHeader == null)
      {
        batch.ChainHeader = Headerchain.ReadHeader(headerHash, batch.SHA256Generator);
      }

      byte[] headerHashValidator;

      if (batch.ChainHeader.HeadersNext == null)
      {
        headerHashValidator = batch.ChainHeader.GetHeaderHash(batch.SHA256Generator);
      }
      else
      {
        batch.ChainHeader = batch.ChainHeader.HeadersNext[0];
        headerHashValidator = batch.ChainHeader.NetworkHeader.HashPrevious;
      }

      if (!headerHashValidator.IsEqual(headerHash))
      {
        throw new UTXOException(string.Format("Unexpected header hash {0}, \nexpected {1}",
          headerHash.ToHexString(),
          headerHashValidator.ToHexString()));
      }
    }
  }
}