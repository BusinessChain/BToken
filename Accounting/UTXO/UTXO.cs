using System.Diagnostics;

using System;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using System.Threading.Tasks;
using System.Runtime.Remoting.Metadata.W3cXsd2001;

using BToken.Chaining;
using BToken.Networking;

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
    TimeSpan AccumulatedLoadTime;

    const int SIZE_BATCH_BLOCKS = 8;
    const int COUNT_BATCHES_PARALLEL = 4;
    byte[][] QueueMergeBlockBatches = new byte[COUNT_BATCHES_PARALLEL][];
    readonly object BatchIndexLOCK = new object();
    int BatchIndex = 0;

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
      //  var uTXOBuilder = new UTXOBuilder(
      //    this,
      //    new Headerchain.HeaderStream(Headerchain));

      //  await uTXOBuilder.BuildAsync();

      await BuildAsync();
    }
    async Task BuildAsync()
    {
      var stopWatchBuild = new Stopwatch();
      var stopWatchGetBlocks = new Stopwatch();

      stopWatchBuild.Start();

      Console.WriteLine(
        "BatchIndex," +
        "PrimaryCacheCompressed," +
        "SecondaryCacheCompressed," +
        "PrimaryCache," +
        "SecondaryCache," +
        "Merge time");

      var headerHashesBatches = new UInt256[COUNT_BATCHES_PARALLEL][];
      int batchIndexOffset = 0;
      do
      {
        Console.WriteLine("Start merging blocks for batch '{0}'", batchIndexOffset);
        stopWatchGetBlocks.Restart();

        Parallel.For(0, COUNT_BATCHES_PARALLEL,
          async i => {
            byte[] blocks = await GetBlockBatchAsync(batchIndexOffset + i);
            
            MergeBatch(i, blocks);
          });

        stopWatchGetBlocks.Stop();
        Console.WriteLine("Finished merging blocks for batchOffset '{0}', time: '{1}'",
          batchIndexOffset,
          stopWatchGetBlocks.Elapsed);

        batchIndexOffset += COUNT_BATCHES_PARALLEL;
      } while (!headerHashesBatches.Any(h => h == null));

    }

    void MergeBatch(int batchIndex, byte[] blockBatchBytes)
    {
      lock (BatchIndexLOCK)
      {
        if (BatchIndex != batchIndex)
        {
          QueueMergeBlockBatches[batchIndex] = blockBatchBytes;
          return;
        }
      }

      var stopWatchMergeBatch = new Stopwatch();
      while (true)
      {
        stopWatchMergeBatch.Restart();
        
        int startIndex = 0;

        var block = Block.TryReadBlock(blockBatchBytes, ref startIndex);

        for (int b = 0; b < SIZE_BATCH_BLOCKS; b++)
        {
          List<TX> tXs = block.TXs;
          List<byte[]> tXHashes = block.TXHashes;
          byte[] headerHashBytes = block.HeaderHash.GetBytes();

          for (int t = 0; t < tXs.Count; t++)
          {
            // debug

            byte[] outputTXHash = new byte[tXHashes[t].Length];
            tXHashes[t].CopyTo(outputTXHash, 0);
            Array.Reverse(outputTXHash);
            if (new SoapHexBinary(outputTXHash).ToString() == "C02D4826DEE0F0A810E9DC3DB49A484CDF90832C56991F0EBA88418B80C7EC29")
            {
              byte[] inputTXHash = new byte[tXHashes[t].Length];
              tXHashes[t].CopyTo(inputTXHash, 0);
              Array.Reverse(inputTXHash);

              Console.WriteLine("Write outputs of TX '{0}' to UTXO",
                new SoapHexBinary(outputTXHash));
            }

            // end debug

            UTXO.InsertUTXO(tXHashes[t], headerHashBytes, tXs[t].Outputs.Count);
          }

          for (int t = 1; t < tXs.Count; t++)
          {
            for (int i = 0; i < tXs[t].Inputs.Count; i++)
            {
              try
              {
                // debug

                byte[] outputTXHash = new byte[tXHashes[t].Length];
                tXs[t].Inputs[i].TXIDOutput.CopyTo(outputTXHash, 0);
                Array.Reverse(outputTXHash);
                string outputTXHashString = new SoapHexBinary(outputTXHash).ToString();

                if (outputTXHashString == "C02D4826DEE0F0A810E9DC3DB49A484CDF90832C56991F0EBA88418B80C7EC29")
                {
                  byte[] inputTXHash = new byte[tXHashes[t].Length];
                  tXHashes[t].CopyTo(inputTXHash, 0);
                  Array.Reverse(inputTXHash);

                  Console.WriteLine("Input '{0}' in TX '{1}' \n attempts to spend " +
                    "output '{2}' in TX '{3}'.",
                    i,
                    new SoapHexBinary(inputTXHash),
                    tXs[t].Inputs[i].IndexOutput,
                    new SoapHexBinary(outputTXHash));
                }

                // end debug


                UTXO.SpendUTXO(
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

        stopWatchMergeBatch.Stop();

        Console.WriteLine("{0},{1},{2},{3},{4}",
          ((UTXOCacheUInt32)Caches[IndexCacheUInt32]).GetCountPrimaryCacheItems(),
          ((UTXOCacheUInt32)Caches[IndexCacheUInt32]).GetCountSecondaryCacheItems(),
          ((UTXOCacheByteArray)Caches[IndexCacheByteArray]).GetCountPrimaryCacheItems(),
          ((UTXOCacheByteArray)Caches[IndexCacheByteArray]).GetCountSecondaryCacheItems(),
          stopWatchMergeBatch.ElapsedMilliseconds);

        lock (BatchIndexLOCK)
        {
          BatchIndex = (BatchIndex + 1) % COUNT_BATCHES_PARALLEL;
          blockBatchBytes = QueueMergeBlockBatches[BatchIndex];

          if (blockBatchBytes == null)
          {
            return;
          }

          QueueMergeBlockBatches[BatchIndex] = null;
        }
      }
    }

    async Task<byte[]> GetBlockBatchAsync(int batchIndex)
    {
      try
      {
        if (BlockArchiver.Exists(batchIndex, out string filePath))
        {
          byte[] blockBatchBytes = await BlockArchiver.ReadBlockBatchAsync(filePath);
          int batchByteIndex = 0;

          int countBlocks = VarInt.ParseVarInt32(blockBatchBytes, ref batchByteIndex);
          var networkBlocks = new NetworkBlock[countBlocks];
          var blocks = new Block[countBlocks];

          for (int i = 0; i < countBlocks; i++)
          {
            networkBlocks[i] = NetworkBlock.ReadBlock(blockBatchBytes, ref batchByteIndex);

            ValidateNetworkBlock(
              networkBlock,
              hash,
              out Block block);

            blocks[i] = new Block(networkBlock.Header, hash, tXs, tXHashes);
          }
        }
        else
        {
          blocks[i] = await DownloadBlockAsync(hash);
        }
      }
      catch (UTXOException)
      {
        BlockArchiver.DeleteBlock(hash);

        blocks[i] = await DownloadBlockAsync(hash);
      }
    }

    void ValidateNetworkBlock(
      NetworkBlock networkBlock, 
      UInt256 hash)
    {
      if (!hash.Equals(networkBlock.Header.ComputeHash()))
      {
        throw new UTXOException("Unexpected header hash.");
      }

      List<TX> tXs = Parser.Parse(networkBlock.Payload);
      if (!networkBlock.Header.MerkleRoot.Equals(Parser.ComputeMerkleRootHash(tXs, out List<byte[]> tXHashes)))
      {
        throw new UTXOException("Payload corrupted.");
      }
    }
    async Task<Block> DownloadBlockAsync(UInt256 hash)
    {
      var sessionBlockDownload = new SessionBlockDownload(hash);
      await Network.ExecuteSessionAsync(sessionBlockDownload);

      ValidateNetworkBlock(
        sessionBlockDownload.Block,
        hash,
        out Block block);

      await BlockArchiver.ArchiveBlockAsync(block);

      return block;
    }
    static void ValidateHeaderHash(NetworkHeader header, UInt256 hash)
    {
      if (!hash.Equals(header.ComputeHash()))
      {
        throw new UTXOException("Unexpected header hash.");
      }
    }
    void ValidateMerkleRoot(UInt256 merkleRoot, List<TX> tXs, out List<byte[]> tXHashes)
    {
      if (!merkleRoot.Equals(Parser.ComputeMerkleRootHash(tXs, out tXHashes)))
      {
        throw new UTXOException("Payload corrupted.");
      }
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