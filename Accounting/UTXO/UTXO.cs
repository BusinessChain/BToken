using System.Diagnostics;

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
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
    UTXOCache Cache;

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

      Cache =
        new UTXOCacheUInt32(
          new UTXOCacheULong64(
            new UTXOCacheByteArray()));
      Cache.Initialize();
    }

    public async Task StartAsync()
    {
      try
      {
        await Cache.LoadFromDiskAsync();
        await LoadBatchHeight();
      }
      catch
      {
        Cache.Clear();
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
          Cache.GetLabelsMetricsCSV());

        Console.WriteLine(labelsCSV);
        BuildWriter.WriteLine(labelsCSV);

        UTCTimeStartup = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        
        Parallel.For(0, COUNT_BATCHES_PARALLEL, i =>
        {
          Task mergeBatchTask = MergeBatchAsync(i);
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
            BatchBlockLoad batchCompleted = await await Task.WhenAny(
              BatchesQueued.Select(b => b.SignalBatchCompletion.Task));

            BatchesQueued.Remove(batchCompleted);
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

    async Task MergeBatchAsync(int mergerID)
    {
      while (true)
      {
        BatchBlockLoad batch = await BatchQueue.ReceiveAsync().ConfigureAwait(false);

        batch.StopwatchParse.Start();
        ParseBlocks(batch);
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

          BackupToDisk();

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
      if (IndexBatchMerge % CACHE_ARCHIVING_INTERVAL == 0 && IndexBatchMerge > 0)
      {
        Directory.CreateDirectory(RootPath);

        byte[] heights = new byte[8];
        BitConverter.GetBytes(IndexBatchMerge).CopyTo(heights, 0);
        BitConverter.GetBytes(BlockHeight).CopyTo(heights, 4);

        Task backupBlockHeightTask = WriteFileAsync(
          Path.Combine(RootPath, "BatchHeight"),
          heights);

        Cache.BackupToDisk();
      }
    }
    void BatchReporting(BatchBlockLoad batch, int mergerID)
    {
      batch.SignalBatchCompletion.SetResult(batch);
      
      long timeParsePlusMerge = batch.StopwatchMerging.ElapsedMilliseconds + batch.StopwatchParse.ElapsedMilliseconds;
      int ratio = (int)((float)batch.StopwatchMerging.ElapsedTicks * 100 / batch.StopwatchParse.ElapsedTicks);

      string metricsCSV = string.Format("{0},{1},{2},{3},{4},{5}",
        batch.Index,
        BlockHeight,
        DateTimeOffset.UtcNow.ToUnixTimeSeconds() - UTCTimeStartup,
        timeParsePlusMerge,
        ratio,
        Cache.GetMetricsCSV());

      Console.WriteLine(metricsCSV);
      BuildWriter.WriteLine(metricsCSV);
    }

    void Merge(List<Block> blocks)
    {
      foreach (Block block in blocks)
      {
        for (int t = 0; t < block.TXs.Length; t += 1)
        {

          //if (block.TXs[t].Hash.ToHexString() == "6BADC627C7D591C7B4977821BF777974E37F49627A225E3D147F13031AFC9EA0"
          //  && block.TXs[t].Outputs.Length == 0)
          //{ }

          Cache.InsertUTXO(
            block.TXs[t].Hash,
            block.HeaderHash,
            block.TXs[t].Outputs.Length);
        }

        for (int t = 1; t < block.TXs.Length; t += 1)
        {
          for (int i = 0; i < block.TXs[t].Inputs.Length; i += 1)
          {
            try
            {
              //if (block.TXs[t].Inputs[i].TXIDOutput.ToHexString() == "14356E1A441272DF26D8F06D1BFA1549F5EDA51B5768096CF626A3371815B6EA"
              //  && block.TXs[t].Inputs[i].IndexOutput == 0)
              //{ }

              Cache.SpendUTXO(
                block.TXs[t].Inputs[i].TXIDOutput,
                block.TXs[t].Inputs[i].IndexOutput);
            }
            catch (UTXOException ex)
            {
              byte[] inputTXHash = new byte[HASH_BYTE_SIZE];
              block.TXs[t].Hash.CopyTo(inputTXHash, 0);
              Array.Reverse(inputTXHash);

              byte[] outputTXHash = new byte[HASH_BYTE_SIZE];
              block.TXs[t].Inputs[i].TXIDOutput.CopyTo(outputTXHash, 0);
              Array.Reverse(outputTXHash);

              Console.WriteLine("Input {0} in TX {1} \n failed to spend output " +
                "{2} in TX {3}: \n{4}.",
                i,
                new SoapHexBinary(inputTXHash),
                block.TXs[t].Inputs[i].IndexOutput,
                new SoapHexBinary(outputTXHash),
                ex.Message);

              throw ex;
            }
          }
        }

        BlockHeight += 1;
      }
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


    static byte[] ComputeMerkleRootHash(
    byte[] buffer,
    ref int bufferIndex,
    TX[] tXs,
    SHA256 sHA256Generator)
    {
      if (tXs.Length == 1)
      {
        tXs[0] = TX.Parse(buffer, ref bufferIndex, sHA256Generator);
        return tXs[0].Hash;
      }

      int tXsLengthMod2 = tXs.Length & 1;

      var merkleList = new byte[tXs.Length + tXsLengthMod2][];

      for (int t = 0; t < tXs.Length; t++)
      {
        tXs[t] = TX.Parse(buffer, ref bufferIndex, sHA256Generator);
        merkleList[t] = tXs[t].Hash;
      }

      if (tXsLengthMod2 != 0)
      {
        merkleList[tXs.Length] = merkleList[tXs.Length - 1];
      }

      return GetRoot(merkleList, sHA256Generator);
    }

    static byte[] GetRoot(
      byte[][] merkleList,
      SHA256 sHA256Generator)
    {
      int merkleIndex = merkleList.Length;

      while (true)
      {
        merkleIndex >>= 1;

        if (merkleIndex == 1)
        {
          return ComputeNextMerkleList(merkleList, merkleIndex, sHA256Generator)[0];
        }

        merkleList = ComputeNextMerkleList(merkleList, merkleIndex, sHA256Generator);

        if ((merkleIndex & 1) != 0)
        {
          merkleList[merkleIndex] = merkleList[merkleIndex - 1];
          merkleIndex += 1;
        }
      }

    }

    static byte[][] ComputeNextMerkleList(
      byte[][] merkleList,
      int merkleIndex,
      SHA256 sHA256Generator)
    {
      byte[] leafPair = new byte[TWICE_HASH_BYTE_SIZE];

      for (int i = 0; i < merkleIndex; i++)
      {
        int i2 = i << 1;
        merkleList[i2].CopyTo(leafPair, 0);
        merkleList[i2 + 1].CopyTo(leafPair, HASH_BYTE_SIZE);

        merkleList[i] = sHA256Generator.ComputeHash(
          sHA256Generator.ComputeHash(
            leafPair));
      }

      return merkleList;
    }

    void ParseBlocks(BatchBlockLoad batch)
    {
      try
      {
        int bufferIndex = 0;
        while (bufferIndex < batch.Buffer.Length)
        {
          var block = new Block();

          block.HeaderHash =
          batch.SHA256Generator.ComputeHash(
            batch.SHA256Generator.ComputeHash(
              batch.Buffer,
              bufferIndex,
              COUNT_HEADER_BYTES));

          if (batch.ChainHeader == null)
          {
            batch.ChainHeader = Headerchain.ReadHeader(block.HeaderHash, batch.SHA256Generator);
          }
          ValidateHeaderHash(block.HeaderHash, ref batch.ChainHeader, batch.SHA256Generator);

          int indexMerkleRoot = bufferIndex + OFFSET_INDEX_MERKLE_ROOT;

          bufferIndex += COUNT_HEADER_BYTES;

          int tXCount = VarInt.GetInt32(batch.Buffer, ref bufferIndex);

          block.TXs = new TX[tXCount];

          byte[] merkleRootHash = ComputeMerkleRootHash(
            batch.Buffer,
            ref bufferIndex,
            block.TXs,
            batch.SHA256Generator);

          if (!merkleRootHash.IsEqual(batch.Buffer, indexMerkleRoot))
          {
            throw new UTXOException("Payload corrupted.");
          }

          batch.Blocks.Add(block);
        }
      }
      catch (Exception ex)
      {
        Console.WriteLine("Block parsing threw exception: " + ex.Message);
      }
    }

    static void ValidateHeaderHash(
      byte[] headerHash,
      ref Headerchain.ChainHeader chainHeader,
      SHA256 sHA256Generator)
    {
      byte[] headerHashValidator;

      if (chainHeader.HeadersNext == null)
      {
        headerHashValidator = chainHeader.GetHeaderHash(sHA256Generator);
      }
      else
      {
        chainHeader = chainHeader.HeadersNext[0];
        headerHashValidator = chainHeader.NetworkHeader.HashPrevious;
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