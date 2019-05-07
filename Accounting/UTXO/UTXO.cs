using System.Diagnostics;

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Runtime.Remoting.Metadata.W3cXsd2001;

using BToken.Chaining;
using BToken.Networking;
using System.Security.Cryptography;

namespace BToken.Accounting
{
  public partial class UTXO
  {
    Headerchain Headerchain;
    Network Network;
    UTXOArchiver Archiver;

    UTXOCache Cache;

    const int COUNT_HEADER_BYTES = 80;
    const int OFFSET_INDEX_HASH_PREVIOUS = 4;
    const int OFFSET_INDEX_MERKLE_ROOT = 36;
    const int HASH_BYTE_SIZE = 32;
    const int TWICE_HASH_BYTE_SIZE = HASH_BYTE_SIZE << 1;

    const int COUNT_INTEGER_BITS = sizeof(int) * 8;
    const int COUNT_HEADERINDEX_BITS = 26;
    const int COUNT_COLLISION_BITS = 3;

    static readonly int CountHeaderPlusCollisionBits = COUNT_HEADERINDEX_BITS + COUNT_COLLISION_BITS;

    static long UTCTimeStartup;
    Stopwatch[] Stopwatchs = new Stopwatch[COUNT_BATCHES_PARALLEL];

    const int COUNT_BATCHES_PARALLEL = 4;
    bool ParallelBatchesExistInArchive = true;
    Dictionary<int, BatchBlockLoad> QueueBlocksMerge = new Dictionary<int, BatchBlockLoad>();
    readonly object MergeLOCK = new object();
    int IndexBatchMerge = 0;
    StreamWriter BuildWriter;

    const int COUNT_BLOCK_DOWNLOAD_BATCH = 2;
    const int COUNT_DOWNLOAD_TASKS = 8;

    List<Block> BlocksPartitioned = new List<Block>();
    int CountTXsPartitioned = 0;
    int FilePartitionIndex = 0;
    const int MAX_COUNT_TXS_IN_PARTITION = 10000;
    
    Headerchain.ChainHeader ChainHeader;

    int BlockHeight = 0;


    public UTXO(Headerchain headerchain, Network network)
    {
      Headerchain = headerchain;
      ChainHeader = Headerchain.GenesisHeader;
      Network = network;

      Cache = new UTXOCacheUInt32(
        new UTXOCacheByteArray());
      Cache.Initialize();

      Archiver = new UTXOArchiver();

      for (int i = 0; i < Stopwatchs.Length; i++)
      {
        Stopwatchs[i] = new Stopwatch();
      }
    }

    public async Task StartAsync()
    {
      await BuildAsync();
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
        "Merge time," +
        "Block height," +
        "Total block bytes loaded," +
        Cache.GetLabelsMetricsCSV(),
        "Merge time");

        UTCTimeStartup = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

        Console.WriteLine(labelsCSV);
        BuildWriter.WriteLine(labelsCSV);

        await BuildFromArchive();
        await BuildFromNetwork();
      }
    }

    async Task BuildFromArchive()
    {
      int batchIndexOffset = 0;
      ParallelBatchesExistInArchive = true;

      Task[] loadTasks = new Task[COUNT_BATCHES_PARALLEL];

      while (ParallelBatchesExistInArchive)
      {
        Parallel.For(0, COUNT_BATCHES_PARALLEL, i =>
        {
          loadTasks[i] = LoadAsync(
            new BatchBlockLoad(batchIndexOffset + i));
        });

        await Task.WhenAll(loadTasks);

        batchIndexOffset += COUNT_BATCHES_PARALLEL;
      }
    }

    async Task LoadAsync(BatchBlockLoad batch)
    {
      if (BlockArchiver.Exists(batch.BatchIndex, out string filePath))
      {
        byte[] blockBuffer = await BlockArchiver.ReadBlockBatchAsync(filePath).ConfigureAwait(false);

        batch.Blocks = ParseBlocks(batch, blockBuffer);

        lock (MergeLOCK)
        {
          if (IndexBatchMerge != batch.BatchIndex)
          {
            QueueBlocksMerge.Add(batch.BatchIndex, batch);
            return;
          }
        }

        Merge(
          batch,
          flagArchive: false);
      }
      else
      {
        ParallelBatchesExistInArchive = false;
      }
    }

    void Merge(BatchBlockLoad batch, bool flagArchive)
    {
      while (true)
      {
        foreach (Block block in batch.Blocks)
        {
          for (int t = 0; t < block.TXs.Length; t++)
          {
            Cache.InsertUTXO(
              block.TXs[t].Hash,
              block.HeaderHash,
              block.TXs[t].Outputs.Length);
          }

          for (int t = 1; t < block.TXs.Length; t++)
          {
            for (int i = 0; i < block.TXs[t].Inputs.Length; i++)
            {
              try
              {
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

        if(flagArchive)
        {
          ArchiveBatch(batch.BatchIndex, batch.Blocks);
        }

        string metricsCSV = string.Format("{0},{1},{2},{3}",
          IndexBatchMerge,
          BlockHeight,
          DateTimeOffset.UtcNow.ToUnixTimeSeconds() - UTCTimeStartup,
          Cache.GetMetricsCSV());

        Console.WriteLine(metricsCSV);
        BuildWriter.WriteLine(metricsCSV);

        lock (MergeLOCK)
        {
          IndexBatchMerge += 1;
          ChainHeader = batch.ChainHeader;

          if (QueueBlocksMerge.TryGetValue(IndexBatchMerge, out batch))
          {
            QueueBlocksMerge.Remove(IndexBatchMerge);
            continue;
          }

          return;
        }
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
      if(chainHeader == null)
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

    async Task BuildFromNetwork()
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
      if(tXs.Length == 1)
      {
        tXs[0] = TX.Parse(buffer, ref bufferIndex, sHA256Generator);
        return tXs[0].Hash;
      }

      int tXsLengthMod2 = tXs.Length & 1;

      var merkleList = new byte[tXs.Length + tXsLengthMod2][];

      ComputeTXHashes(
        buffer,
        ref bufferIndex,
        tXs,
        merkleList,
        sHA256Generator);

      if(tXsLengthMod2 != 0)
      {
        merkleList[tXs.Length] = merkleList[tXs.Length - 1];
      }

      return GetRoot(merkleList, sHA256Generator);
    }

    static void ComputeTXHashes(
      byte[] buffer,
      ref int bufferIndex,
      TX[] tXs,
      byte[][] merkleList,
      SHA256 sHA256Generator)
    {
      for (int t = 0; t < tXs.Length; t++)
      {
        tXs[t] = TX.Parse(buffer, ref bufferIndex, sHA256Generator);
        merkleList[t] = tXs[t].Hash;
      }
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
          merkleIndex++;
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

    List<Block> ParseBlocks(BatchBlockLoad batch, byte[] blockBuffer)
    {
      var blocks = new List<Block>();

      try
      {
        int bufferIndex = 0;
        while (bufferIndex < blockBuffer.Length)
        {
          var block = new Block();
          
          block.HeaderHash =
          batch.SHA256Generator.ComputeHash(
            batch.SHA256Generator.ComputeHash(
              blockBuffer,
              bufferIndex,
              COUNT_HEADER_BYTES));

          if (batch.ChainHeader == null)
          {
            batch.ChainHeader = Headerchain.ReadHeader(block.HeaderHash, batch.SHA256Generator);
          }
          ValidateHeaderHash(block.HeaderHash, ref batch.ChainHeader, batch.SHA256Generator);

          int indexMerkleRoot = bufferIndex + OFFSET_INDEX_MERKLE_ROOT;

          bufferIndex += COUNT_HEADER_BYTES;

          int tXCount = VarInt.GetInt32(blockBuffer, ref bufferIndex);

          block.TXs = new TX[tXCount];

          byte[] merkleRootHash = ComputeMerkleRootHash(
            blockBuffer,
            ref bufferIndex,
            block.TXs,
            batch.SHA256Generator);

          if (!merkleRootHash.IsEqual(blockBuffer, indexMerkleRoot))
          {
            throw new UTXOException("Payload corrupted.");
          }

          blocks.Add(block);
        }
      }
      catch(Exception ex)
      {
        Console.WriteLine("Block parsing threw exception: " + ex.Message);
      }

      return blocks;
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