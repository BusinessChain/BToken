using System.Diagnostics;

using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using System.Runtime.Remoting.Metadata.W3cXsd2001;

using BToken.Chaining;
using BToken.Networking;
using BToken.Hashing;
using System.Security.Cryptography;

namespace BToken.Accounting
{
  public partial class UTXO
  {
    Headerchain Headerchain;
    UTXOParser Parser;
    Network Network;
    UTXOArchiver Archiver;

    UTXOCache Cache;

    const int HASH_BYTE_SIZE = 32;
    const int TWICE_HASH_BYTE_SIZE = HASH_BYTE_SIZE << 1;

    const int COUNT_INTEGER_BITS = sizeof(int) * 8;
    const int COUNT_HEADERINDEX_BITS = 26;
    const int COUNT_COLLISION_BITS = 3;

    static readonly int CountHeaderPlusCollisionBits = COUNT_HEADERINDEX_BITS + COUNT_COLLISION_BITS;

    static long UTCTimeStartup;
    Stopwatch StopWatchMerkle = new Stopwatch();

    SHA256 SHA256Generator = SHA256.Create();
    byte[] LeafPairHashesConcat = new byte[TWICE_HASH_BYTE_SIZE];

    const int COUNT_BATCHES_PARALLEL = 4;
    bool ParallelBatchesExistInArchive = true;
    byte[][] QueueMergeBlockBatches = new byte[COUNT_BATCHES_PARALLEL][];
    Dictionary<int, Block[]> QueueMergeBlocks = new Dictionary<int, Block[]>();
    readonly object MergeLOCK = new object();
    int MergeBatchIndex = 0;
    long CountBlockBytesDownloadedTotal = 0;
    int FilePartitionIndex = 0;
    List<Block> BlocksPartitioned = new List<Block>();
    int CountTXsPartitioned = 0;
    const int MAX_COUNT_TXS_IN_PARTITION = 10000;

    int BlockHeight = 0;
    Headerchain.ChainHeader ChainHeader;


    public UTXO(Headerchain headerchain, Network network)
    {
      Headerchain = headerchain;
      Parser = new UTXOParser();
      Network = network;

      Cache = new UTXOCacheUInt32(
        new UTXOCacheByteArray());
      Cache.Initialize();

      Archiver = new UTXOArchiver();
    }

    public async Task StartAsync()
    {
      await BuildAsync();
    }

    async Task BuildFromArchive(StreamWriter buildWriter)
    {
      int batchIndexOffset = 0;
      ParallelBatchesExistInArchive = true;

      Task[] loadTasks = new Task[COUNT_BATCHES_PARALLEL];

      while (ParallelBatchesExistInArchive)
      {
        Parallel.For(0, COUNT_BATCHES_PARALLEL, i =>
        {
          loadTasks[i] = LoadAsync(batchIndexOffset + i, i, buildWriter);
        });

        await Task.WhenAll(loadTasks);

        batchIndexOffset += COUNT_BATCHES_PARALLEL;
      }
    }
    async Task BuildFromNetwork(StreamWriter buildWriter)
    {
      //int indexBatchDownload = MergeBatchIndex;
      //FilePartitionIndex = MergeBatchIndex;
      //const int COUNT_BLOCK_DOWNLOAD_BATCH = 10;
      //const int COUNT_DOWNLOAD_TASKS = 8;
      //var blockDownloadTasks = new List<Task>(COUNT_DOWNLOAD_TASKS);

      //while (headerStream.TryGetHeaderLocations(
      //  COUNT_BLOCK_DOWNLOAD_BATCH,
      //  out HeaderLocation[] headerLocations))
      //{
      //  var sessionBlockDownload = new SessionBlockDownload(
      //    this,
      //    headerLocations,
      //    indexBatchDownload,
      //    buildWriter);

      //  Task blockDownloadTask = Network.ExecuteSessionAsync(sessionBlockDownload);
      //  blockDownloadTasks.Add(blockDownloadTask);

      //  if (blockDownloadTasks.Count > COUNT_DOWNLOAD_TASKS)
      //  {
      //    Task blockDownloadTaskCompleted = await Task.WhenAny(blockDownloadTasks);
      //    blockDownloadTasks.Remove(blockDownloadTaskCompleted);
      //  }

      //  indexBatchDownload++;
      //}

      //await Task.WhenAll(blockDownloadTasks);
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
        string labelsCSV = string.Format(
        "BatchIndex," +
        "Merge time," +
        "Block height," +
        "Total block bytes loaded," +
        Cache.GetLabelsMetricsCSV(),
        "Merge time");

        UTCTimeStartup = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

        Console.WriteLine(labelsCSV);
        buildWriter.WriteLine(labelsCSV);

        await BuildFromArchive(buildWriter);
        await BuildFromNetwork(buildWriter);
      }
    }

    async Task LoadAsync(
      int batchIndex,
      int queueIndex,
      StreamWriter buildWriter)
    {
      try
      {
        if (BlockArchiver.Exists(batchIndex, out string filePath))
        {
          byte[] blockBatchBytes = await BlockArchiver.ReadBlockBatchAsync(filePath);

          lock (MergeLOCK)
          {
            if (MergeBatchIndex != batchIndex)
            {
              QueueMergeBlockBatches[queueIndex] = blockBatchBytes;
              return;
            }
          }

          while (true)
          {
            Merge(blockBatchBytes);

            string metricsCSV = string.Format("{0},{1},{2},{3},{4},{5}",
              batchIndex,
              DateTimeOffset.UtcNow.ToUnixTimeSeconds() - UTCTimeStartup,
              BlockHeight,
              0,
              Cache.GetMetricsCSV(),
              StopWatchMerkle.ElapsedMilliseconds / 1000);

            Console.WriteLine(metricsCSV);
            buildWriter.WriteLine(metricsCSV);

            lock (MergeLOCK)
            {
              MergeBatchIndex++;
              queueIndex++;
              batchIndex++;

              if (queueIndex == COUNT_BATCHES_PARALLEL ||
                QueueMergeBlockBatches[queueIndex] == null)
              {
                return;
              }

              blockBatchBytes = QueueMergeBlockBatches[queueIndex];
              QueueMergeBlockBatches[queueIndex] = null;
            }
          }
        }
        else
        {
          ParallelBatchesExistInArchive = false;
        }
      }
      catch (Exception ex)
      {
        Console.WriteLine(ex.Message);
      }
    }

    void Merge(byte[] blockBytes)
    {
      int startIndex = 0;

      while (startIndex < blockBytes.Length)
      {
        NetworkHeader header = NetworkHeader.ParseHeader(
          blockBytes,
          out int tXCount,
          ref startIndex);

        ValidateHeader(header, out byte[] headerHashBytes);

        StopWatchMerkle.Start();

        TX[] tXs = ParseBlock(
          blockBytes,
          ref startIndex,
          tXCount);

        ValidateBlock(tXs, header.MerkleRoot, out byte[][] tXHashes);

        Merge(tXs, tXHashes, headerHashBytes);

        StopWatchMerkle.Stop();

        BlockHeight++;
      }
    }
    void ValidateBlock(TX[] tXs, byte[] merkleRoot, out byte[][] tXHashes)
    {
      byte[] merkleRootHash = ComputeMerkleRootHash(tXs, out tXHashes);

      if (!EqualityComparerByteArray.IsEqual(merkleRoot, merkleRootHash))
      {
        throw new UTXOException("Payload corrupted.");
      }
    }
    void ValidateHeader(NetworkHeader header, out byte[] headerHashBytes)
    {
      UInt256 headerHash = header.ComputeHash(out headerHashBytes);
      UInt256 headerHashInChain;

      if (ChainHeader == null)
      {
        ChainHeader = Headerchain.ReadHeader(headerHash, headerHashBytes);
      }

      if (ChainHeader.HeadersNext == null)
      {
        headerHashInChain = ChainHeader.NetworkHeader.ComputeHash();
      }
      else
      {
        ChainHeader = ChainHeader.HeadersNext[0];
        headerHashInChain = ChainHeader.NetworkHeader.HashPrevious;
      }

      if (!headerHash.Equals(headerHashInChain))
      {
        throw new UTXOException(string.Format("Unexpected header hash {0}",
          headerHash));
      }
    }
    TX[] ParseBlock(
      byte[] buffer,
      ref int startIndex,
      int tXCount)
    {
      var tXs = new TX[tXCount];
      for (int i = 0; i < tXCount; i++)
      {
        tXs[i] = TX.Parse(buffer, ref startIndex);
      }

      return tXs;
    }
    void Merge(TX[] tXs, byte[][] tXHashes, byte[] headerHashBytes)
    {
      for (int t = 0; t < tXs.Length; t++)
      {
        Cache.InsertUTXO(
          tXHashes[t],
          headerHashBytes,
          tXs[t].Outputs.Count);
      }

      for (int t = 1; t < tXs.Length; t++)
      {
        for (int i = 0; i < tXs[t].Inputs.Count; i++)
        {
          try
          {
            Cache.SpendUTXO(
              tXs[t].Inputs[i].TXIDOutput,
              tXs[t].Inputs[i].IndexOutput);
          }
          catch (UTXOException ex)
          {
            byte[] inputTXHash = new byte[tXHashes[t].Length];
            tXHashes[t].CopyTo(inputTXHash, 0);
            Array.Reverse(inputTXHash);

            byte[] outputTXHash = new byte[tXHashes[t].Length];
            tXs[t].Inputs[i].TXIDOutput.CopyTo(outputTXHash, 0);
            Array.Reverse(outputTXHash);

            Console.WriteLine("Input {0} in TX {1} \n failed to spend output " +
              "{2} in TX {3}: \n{4}.",
              i,
              new SoapHexBinary(inputTXHash),
              tXs[t].Inputs[i].IndexOutput,
              new SoapHexBinary(outputTXHash),
              ex.Message);

            throw ex;
          }
        }
      }
    }

    byte[] ComputeMerkleRootHash(TX[] tXs, out byte[][] tXHashes)
    {
      tXHashes = new byte[tXs.Length][];

      if (tXs.Length == 1)
      {
        tXHashes[0] = SHA256d.Compute(tXs[0].GetBytes());
        return tXHashes[0];
      }
           
      if ((tXs.Length & 1) == 0)
      {
        var merkleList = new byte[tXs.Length][];

        for (int t = 0; t < tXs.Length; t++)
        {
          var hash = SHA256d.Compute(tXs[t].GetBytes());
          tXHashes[t] = hash;
          merkleList[t] = hash;
        }

        return GetRoot(merkleList);
      }
      else
      {
        var merkleList = new byte[tXs.Length + 1][];

        for (int t = 0; t < tXs.Length; t++)
        {
          var hash = SHA256d.Compute(tXs[t].GetBytes());
          tXHashes[t] = hash;
          merkleList[t] = hash;
        }

        merkleList[tXs.Length] = merkleList[tXs.Length - 1];

        return GetRoot(merkleList);
      }

    }
    byte[] GetRoot(byte[][] merkleList)
    {
      int merkleIndex = merkleList.Length;

      while (true)
      {
        merkleIndex >>= 1;

        if (merkleIndex == 1)
        {
          return ComputeNextMerkleList(merkleList, merkleIndex)[0];
        }

        merkleList = ComputeNextMerkleList(merkleList, merkleIndex);

        if ((merkleIndex & 1) != 0)
        {
          merkleList[merkleIndex] = merkleList[merkleIndex - 1];
          merkleIndex++;
        }
      }
    }
    byte[][] ComputeNextMerkleList(byte[][] merkleList, int merkleIndex)
    {
      for (int i = 0; i < merkleIndex; i++)
      {
        int i2 = i << 1;
        merkleList[i2].CopyTo(LeafPairHashesConcat, 0);
        merkleList[i2 + 1].CopyTo(LeafPairHashesConcat, HASH_BYTE_SIZE);

        merkleList[i] = SHA256Generator.ComputeHash(
          SHA256Generator.ComputeHash(
            LeafPairHashesConcat));
      }

      return merkleList;
    }
  }
}