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
    Stopwatch[] Stopwatchs = new Stopwatch[COUNT_BATCHES_PARALLEL];

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


    public UTXO(Headerchain headerchain, Network network)
    {
      Headerchain = headerchain;
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

    async Task BuildFromArchive(StreamWriter buildWriter)
    {
      int batchIndexOffset = 0;
      ParallelBatchesExistInArchive = true;

      Task[] loadTasks = new Task[COUNT_BATCHES_PARALLEL];

      while (ParallelBatchesExistInArchive)
      {
        Parallel.For(0, COUNT_BATCHES_PARALLEL, i =>
        {
          UTXOLoader uTXOLoader = new UTXOLoader(
            this,
            batchIndexOffset + i,
            i,
            buildWriter,
            Stopwatchs[i]);

          loadTasks[i] = uTXOLoader.LoadAsync();
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


    static TX[] ParseBlock(
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

    static byte[] ComputeMerkleRootHash(
    byte[] buffer,
    ref int bufferIndex,
    TX[] tXs,
    SHA256 sHA256Generator,
    Stopwatch stopwatch)
    {
      if(tXs.Length == 1)
      {
        tXs[0] = TX.Parse2(buffer, ref bufferIndex, sHA256Generator);
        return tXs[0].Hash;
      }

      int tXsLengthMod2 = tXs.Length & 1;

      var merkleList = new byte[tXs.Length + tXsLengthMod2][];

      ComputeTXHashes(
        buffer,
        ref bufferIndex,
        tXs,
        merkleList,
        sHA256Generator,
        stopwatch);

      if(tXsLengthMod2 != 0)
      {
        merkleList[tXs.Length] = merkleList[tXs.Length - 1];
      }

      return GetRoot(merkleList, sHA256Generator, stopwatch);
    }

    static void ComputeTXHashes(
      byte[] buffer,
      ref int bufferIndex,
      TX[] tXs,
      byte[][] merkleList,
      SHA256 sHA256Generator,
      Stopwatch stopwatch)
    {
      stopwatch.Start();

      for (int t = 0; t < tXs.Length; t++)
      {
        tXs[t] = TX.Parse2(buffer, ref bufferIndex, sHA256Generator);
        merkleList[t] = tXs[t].Hash;
      }

      stopwatch.Stop();
    }

    static byte[] GetRoot(
      byte[][] merkleList,
      SHA256 sHA256Generator,
      Stopwatch stopwatch)
    {
      int merkleIndex = merkleList.Length;

      while (true)
      {
        merkleIndex >>= 1;

        if (merkleIndex == 1)
        {
          return ComputeNextMerkleList(merkleList, merkleIndex, sHA256Generator, stopwatch)[0];
        }

        merkleList = ComputeNextMerkleList(merkleList, merkleIndex, sHA256Generator, stopwatch);

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
      SHA256 sHA256Generator,
      Stopwatch stopwatch)
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
    
    void Merge(
      byte[] blockBytes,
      TX[] tXs,
      byte[] headerHashBytes)
    {
      for (int t = 0; t < tXs.Length; t++)
      {
        Cache.InsertUTXO(
          tXs[t].Hash,
          headerHashBytes,
          tXs[t].Outputs2.Length);
      }

      for (int t = 1; t < tXs.Length; t++)
      {
        for (int i = 0; i < tXs[t].Inputs2.Length; i++)
        {
          try
          {
            Cache.SpendUTXO(
              tXs[t].Inputs2[i].TXIDOutput,
              tXs[t].Inputs2[i].IndexOutput);
          }
          catch (UTXOException ex)
          {
            byte[] inputTXHash = new byte[HASH_BYTE_SIZE];
            tXs[t].Hash.CopyTo(inputTXHash, 0);
            Array.Reverse(inputTXHash);

            byte[] outputTXHash = new byte[HASH_BYTE_SIZE];
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

      BlockHeight++;
    }


  }
}