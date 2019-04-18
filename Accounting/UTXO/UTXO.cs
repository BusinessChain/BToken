using System.Diagnostics;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Runtime.Remoting.Metadata.W3cXsd2001;

using BToken.Chaining;
using BToken.Networking;
using BToken.Hashing;

namespace BToken.Accounting
{
  public partial class UTXO
  {
    Headerchain Headerchain;
    UTXOParser Parser;
    Network Network;
    UTXOArchiver Archiver;

    UTXOCache[] Caches;

    const int COUNT_INTEGER_BITS = sizeof(int) * 8;
    const int COUNT_HEADERINDEX_BITS = 26;
    const int COUNT_COLLISION_BITS = 3;

    const uint MaskSecondaryCacheUInt32 = 0x04000000;
    const uint MaskCollisionCacheUInt64 = 0x08000000;
    const uint MaskCollisionCacheByteArray = 0x10000000;

    const int IndexCacheUInt32 = 0;
    const int IndexCacheByteArray = 1;

    static readonly uint MaskAllOutputBitsSpent = uint.MaxValue << CountHeaderPlusCollisionBits;
    static readonly int CountNonHeaderBits = COUNT_INTEGER_BITS - COUNT_HEADERINDEX_BITS;

    static readonly int CountHeaderPlusCollisionBits = COUNT_HEADERINDEX_BITS + COUNT_COLLISION_BITS;
    static readonly int CountHeaderBytes = (COUNT_HEADERINDEX_BITS + 7) / 8;
    static readonly int ByteIndexCollisionBits = (CountHeaderPlusCollisionBits + 7) / 8;
    static readonly int CountNonHeaderBitsInByte = 8 - COUNT_HEADERINDEX_BITS % 8;
    static readonly byte MaskHeaderTailBitsInByte = (byte)(byte.MaxValue >> CountNonHeaderBitsInByte);
    static readonly int CountHeaderBitsInByte = COUNT_HEADERINDEX_BITS % 8;
    static readonly int CountTrailingBitsOfCollisionBitsInByte = CountNonHeaderBitsInByte + COUNT_COLLISION_BITS;
    static readonly int OutputBitsByteIndex = CountHeaderPlusCollisionBits / 8;
    static readonly int CountNonOutputsBitsInByte = CountHeaderPlusCollisionBits % 8;
    static readonly byte MaskAllOutputsBitsInByte = (byte)(byte.MaxValue << CountNonOutputsBitsInByte);
    
    Stopwatch StopWatchGetBlock = new Stopwatch();

    const int COUNT_BATCHES_PARALLEL = 4;
    bool ParallelBatchesExistInArchive = true;
    byte[][] QueueMergeBlockBatches = new byte[COUNT_BATCHES_PARALLEL][];
    Dictionary<int, Block[]> QueueMergeBlocks = new Dictionary<int, Block[]>();
    readonly object MergeLOCK = new object();
    int MergeBatchIndex = 0;
    int FilePartitionIndex = 0;
    List<Block> BlocksPartitioned = new List<Block>();
    int CountTXsPartitioned = 0;

    public UTXO(Headerchain headerchain, Network network)
    {
      Debug.Assert(CountHeaderBitsInByte + COUNT_COLLISION_BITS <= 8,
        "Collision bits should not byte overflow, otherwise utxo parsing errors will occur.");

      Headerchain = headerchain;
      Parser = new UTXOParser();
      Network = network;

      Caches = new UTXOCache[2];
      Caches[IndexCacheUInt32] = new UTXOCacheUInt32(Caches);
      Caches[IndexCacheByteArray] = new UTXOCacheByteArray(Caches);

      Archiver = new UTXOArchiver();
    }

    public async Task StartAsync()
    {
      await BuildAsync();
    }
    async Task LoadAsync(int batchIndex, int queueIndex, Headerchain.HeaderStream headerStream)
    {
      if (BlockArchiver.Exists(batchIndex, out string filePath))
      {
        Console.WriteLine("Start reading batch BatchBytes {0}", batchIndex);

        byte[] blockBatchBytes = await BlockArchiver.ReadBlockBatchAsync(filePath);

        Console.WriteLine("Stop reading batch BatchBytes {0}", batchIndex);

        lock (MergeLOCK)
        {
          if (MergeBatchIndex != batchIndex)
          {
            Console.WriteLine("Postpone merge of batch {0}, queueIndex: {1}, awaiting batch {2}",
              batchIndex,
              queueIndex,
              MergeBatchIndex);

            QueueMergeBlockBatches[queueIndex] = blockBatchBytes;
            return;
          }
        }

        while (true)
        {
          MergeBatch(blockBatchBytes, headerStream, batchIndex);

          Console.WriteLine("Successfully merged batch {0}, queueIndex: {1}",
            batchIndex,
            queueIndex);

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

            Console.WriteLine("Follow-up batch {0} to merge, queueIndex: {1}",
              MergeBatchIndex,
              queueIndex);

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
    async Task BuildAsync()
    {
      //Console.WriteLine(
      //  "BatchIndex," +
      //  "PrimaryCacheCompressed," +
      //  "SecondaryCacheCompressed," +
      //  "PrimaryCache," +
      //  "SecondaryCache," +
      //  "Merge time");

      Console.WriteLine("Start block archive load.");

      Task[] loadTasks = new Task[COUNT_BATCHES_PARALLEL];


      var headerStream = new Headerchain.HeaderStream(Headerchain);
      int batchIndexOffset = 0;
      do
      {
        ParallelBatchesExistInArchive = true;

        Parallel.For(0, COUNT_BATCHES_PARALLEL, i => 
        {
          loadTasks[i] = LoadAsync(batchIndexOffset + i, i, headerStream);
        });

        await Task.WhenAll(loadTasks);

        Console.WriteLine("Do loop");

        if (ParallelBatchesExistInArchive)
        {
          batchIndexOffset += COUNT_BATCHES_PARALLEL;
        }
        else
        {
          break;
        }

      } while (true);

      MergeBatchIndex += batchIndexOffset;
      int indexBatchDownload = MergeBatchIndex;
      int blockDownloadTaskIndex = 0;
      FilePartitionIndex = MergeBatchIndex;
      const int COUNT_BLOCK_DOWNLOAD_BATCH = 50;
      const int COUNT_DOWNLOAD_TASKS = 8;
      var blockDownloadTasks = new Task[COUNT_DOWNLOAD_TASKS];
      
      while(headerStream.TryGetHeaderLocations(
        COUNT_BLOCK_DOWNLOAD_BATCH,
        out HeaderLocation[] headerLocations))
      {
        var sessionBlockDownload = new SessionBlockDownload(
          this, 
          headerLocations, 
          indexBatchDownload);

        Task sessionBlockDownloadTask = Network.ExecuteSessionAsync(sessionBlockDownload);
        blockDownloadTasks[blockDownloadTaskIndex] = sessionBlockDownloadTask;

        if (blockDownloadTaskIndex == COUNT_DOWNLOAD_TASKS - 1)
        {
          await Task.WhenAll(blockDownloadTasks);
          blockDownloadTaskIndex = 0;
        }

        blockDownloadTaskIndex++;
        indexBatchDownload++;
      }
      if(blockDownloadTaskIndex == 0)
      {
        return;
      }
      Task[] finalBlockDownloadTasks = new Task[blockDownloadTaskIndex];
      Array.Copy(blockDownloadTasks, finalBlockDownloadTasks, blockDownloadTaskIndex);
      await Task.WhenAll(blockDownloadTasks);
    }

    void MergeBatch(byte[] blockBytes, Headerchain.HeaderStream headerStream, int batchIndex)
    {
      Console.WriteLine("Merge batch {0}", batchIndex);
      var stopWatchMergeBatch = new Stopwatch();
      stopWatchMergeBatch.Restart();

      int startIndex = 0;
      while (startIndex < blockBytes.Length)
      {
        NetworkHeader header = NetworkHeader.ParseHeader(
          blockBytes, 
          out int tXCount, 
          ref startIndex);


        UInt256 headerHash = header.ComputeHash(out byte[] headerHashBytes);
        HeaderLocation headerLocation = headerStream.GetHeaderLocation();
        UInt256 hash = headerLocation.Hash;

        if (!hash.Equals(headerHash))
        {
          throw new UTXOException("Unexpected header hash.");
        }

        Console.WriteLine("parsed header {0}, height {1} in batchIndex {2}",
          headerHash,
          headerLocation.Height,
          batchIndex);

        TX[] tXs = ParseBlock(blockBytes, ref startIndex, tXCount);
        byte[] merkleRootHash = ComputeMerkleRootHash(tXs, out byte[][] tXHashes);
        if (!EqualityComparerByteArray.IsEqual(header.MerkleRoot, merkleRootHash))
        {
          throw new UTXOException("Payload corrupted.");
        }

        Merge(tXs, tXHashes, headerHashBytes);
      }

      stopWatchMergeBatch.Stop();

      Console.WriteLine("{0},{1},{2},{3},{4}",
        ((UTXOCacheUInt32)Caches[IndexCacheUInt32]).GetCountPrimaryCacheItems(),
        ((UTXOCacheUInt32)Caches[IndexCacheUInt32]).GetCountSecondaryCacheItems(),
        ((UTXOCacheByteArray)Caches[IndexCacheByteArray]).GetCountPrimaryCacheItems(),
        ((UTXOCacheByteArray)Caches[IndexCacheByteArray]).GetCountSecondaryCacheItems(),
        stopWatchMergeBatch.ElapsedMilliseconds);
    }
    void Merge(TX[] tXs, byte[][] tXHashes, byte[] headerHashBytes)
    {
      for (int t = 0; t < tXs.Length; t++)
      {
        InsertUTXO(tXHashes[t], headerHashBytes, tXs[t].Outputs.Count);
      }

      for (int t = 1; t < tXs.Length; t++)
      {
        for (int i = 0; i < tXs[t].Inputs.Count; i++)
        {
          try
          {
            SpendUTXO(
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

            Console.WriteLine("Input '{0}' in TX '{1}' \n failed to spend " +
              "output '{2}' in TX '{3}': \n'{4}'.",
              i,
              new SoapHexBinary(inputTXHash),
              tXs[t].Inputs[i].IndexOutput,
              new SoapHexBinary(outputTXHash),
              ex.Message);
          }
        }
      }
    }
    byte[] ComputeMerkleRootHash(TX[] tXs, out byte[][] tXHashes)
    {
      if(tXs.Length == 1)
      {
        tXHashes = new byte[1][];
        tXHashes[0] = SHA256d.Compute(tXs[0].GetBytes());
        return tXHashes[0];
      }

      if (tXs.Length % 2 == 0)
      {
        tXHashes = new byte[tXs.Length][];

        for (int t = 0; t < tXs.Length; t++)
        {
          tXHashes[t] = SHA256d.Compute(tXs[t].GetBytes());
        }
      }
      else
      {
        tXHashes = new byte[tXs.Length + 1][];

        for (int t = 0; t < tXs.Length; t++)
        {
          tXHashes[t] = SHA256d.Compute(tXs[t].GetBytes());
        }

        tXHashes[tXHashes.Length - 1] = tXHashes[tXHashes.Length - 2];
      }

      return GetRoot(tXHashes);
    }
    byte[] GetRoot(byte[][] merkleList)
    {
      byte[][] merkleListNext;

      while (true)
      {
        int lengthMerkleListNext = merkleList.Length / 2;

        if(lengthMerkleListNext == 1)
        {
          return ComputeNextMerkleList(merkleList, lengthMerkleListNext)[0];
        }
        if (lengthMerkleListNext % 2 == 0)
        {
          merkleListNext = ComputeNextMerkleList(merkleList, lengthMerkleListNext);
        }
        else
        {
          lengthMerkleListNext++;

          merkleListNext = ComputeNextMerkleList(merkleList, lengthMerkleListNext);

          merkleListNext[merkleListNext.Length - 1] = merkleListNext[merkleListNext.Length - 2];
        }

        merkleList = merkleListNext;
      }
    }
    byte[][] ComputeNextMerkleList(byte[][] merkleList, int lengthMerkleListNext)
    {
      var merkleListNext = new byte[lengthMerkleListNext][];

      for (int i = 0; i < merkleList.Length; i += 2)
      {
        const int HASH_BYTE_SIZE = 32;
        var leafPairHashesConcat = new byte[2 * HASH_BYTE_SIZE];
        merkleList[i].CopyTo(leafPairHashesConcat, 0);
        merkleList[i + 1].CopyTo(leafPairHashesConcat, HASH_BYTE_SIZE);

        merkleListNext[i / 2] = SHA256d.Compute(leafPairHashesConcat);
      }

      return merkleListNext;
    }

    TX[] ParseBlock(byte[] buffer, ref int startIndex, int tXCount)
    {
      var tXs = new TX[tXCount];
      for (int i = 0; i < tXCount; i++)
      {
        UInt32 version = BitConverter.ToUInt32(buffer, startIndex);

        tXs[i] = TX.Parse(buffer, ref startIndex);
      }

      return tXs;
    }

    void SpendUTXO(byte[] tXIDOutput, int outputIndex)
    {
      int primaryKey = BitConverter.ToInt32(tXIDOutput, 0);

      for (int i = 0; i < Caches.Length; i++)
      {
        if (Caches[i].TrySpend(primaryKey, tXIDOutput, outputIndex))
        {
          return;
        }
      }

      throw new UTXOException("Referenced TXID not found in UTXO table.");
    }

    static string Bytes2HexStringReversed(byte[] bytes)
    {
      var bytesReversed = new byte[bytes.Length];
      bytes.CopyTo(bytesReversed, 0);
      Array.Reverse(bytesReversed);
      return new SoapHexBinary(bytesReversed).ToString();
    }

    bool TrySetCollisionBit(int primaryKey, int collisionIndex)
    {
      for (int i = 0; i < Caches.Length; i++)
      {
        if (Caches[i].TrySetCollisionBit(primaryKey, collisionIndex))
        {
          return true;
        }
      }
      return false;
    }
    public void InsertUTXO(byte[] tXIDHash, byte[] headerHashBytes, int outputsCount)
    {
      int primaryKey = BitConverter.ToInt32(tXIDHash, 0);
      int lengthUTXOBits = CountHeaderPlusCollisionBits + outputsCount;

      if (lengthUTXOBits <= COUNT_INTEGER_BITS)
      {
        uint uTXO = CreateUTXOInt32(headerHashBytes, outputsCount, lengthUTXOBits);

        if (TrySetCollisionBit(primaryKey, IndexCacheUInt32))
        {
          ((UTXOCacheUInt32)Caches[IndexCacheUInt32]).Write(tXIDHash, uTXO);
        }
        else
        {
          ((UTXOCacheUInt32)Caches[IndexCacheUInt32]).Write(primaryKey, uTXO);
        }
      }
      else
      {
        byte[] uTXO = CreateUTXOByteArray(headerHashBytes, outputsCount, lengthUTXOBits);

        if (TrySetCollisionBit(primaryKey, IndexCacheByteArray))
        {
          ((UTXOCacheByteArray)Caches[IndexCacheByteArray]).Write(tXIDHash, uTXO);
        }
        else
        {
          ((UTXOCacheByteArray)Caches[IndexCacheByteArray]).Write(primaryKey, uTXO);
        }
      }
    }
    static byte[] CreateUTXOByteArray(byte[] headerHashBytes, int outputsCount, int lengthUTXOBits)
    {
      byte[] uTXOIndex;
      if (lengthUTXOBits <= COUNT_INTEGER_BITS)
      {
        uTXOIndex = new byte[sizeof(uint)];
      }
      else
      {
        uTXOIndex = new byte[(lengthUTXOBits + 7) / 8];
      }

      var lengthHeaderIndexBytes = (COUNT_HEADERINDEX_BITS + 7) / 8;
      Array.Copy(headerHashBytes, uTXOIndex, lengthHeaderIndexBytes);

      var numberOfHeaderTailBits = lengthHeaderIndexBytes % 8;
      if (numberOfHeaderTailBits > 0)
      {
        for (int i = numberOfHeaderTailBits; i < 8; i++)
        {
          uTXOIndex[lengthHeaderIndexBytes] &= (byte)~(1 << i);
        }
      }

      return uTXOIndex;
    }
    static uint CreateUTXOInt32(byte[] headerHashBytes, int outputsCount, int lengthUTXOBits)
    {
      uint uTXO = 0;

      for (int i = CountHeaderBytes; i > 0; i--)
      {
        uTXO <<= 8;
        uTXO |= headerHashBytes[i - 1];
      }
      uTXO <<= CountNonHeaderBits;
      uTXO >>= CountNonHeaderBits;

      if (lengthUTXOBits < COUNT_INTEGER_BITS)
      {
        uint maskSpendExcessOutputBits = uint.MaxValue << lengthUTXOBits;
        uTXO |= maskSpendExcessOutputBits;
      }

      return uTXO;
    }

  }
}