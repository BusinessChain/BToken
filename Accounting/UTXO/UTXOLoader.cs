using System;
using System.Diagnostics;
using System.IO;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Security.Cryptography;

using BToken.Networking;
using BToken.Chaining;

namespace BToken.Accounting
{
  public partial class UTXO
  {
    class UTXOLoader
    {
      const int COUNT_HEADER_BYTES = 80;
      const int OFFSET_INDEX_HASH_PREVIOUS = 4;
      const int OFFSET_INDEX_MERKLE_ROOT = 36;
      UTXO UTXO;

      int BatchIndex;
      StreamWriter BuildWriter;
      
      Stopwatch Stopwatch;
      SHA256 SHA256Generator = SHA256.Create();

      Headerchain.ChainHeader ChainHeader;


      public UTXOLoader(
        UTXO uTXO,
        int batchIndex,
        int queueIndex,
        StreamWriter buildWriter,
        Stopwatch stopwatch)
      {
        UTXO = uTXO;
        BatchIndex = batchIndex;
        BuildWriter = buildWriter;

        Stopwatch = stopwatch;
      }

      public async Task LoadAsync()
      {
        try
        {
          if (BlockArchiver.Exists(BatchIndex, out string filePath))
          {
            byte[] buffer = await BlockArchiver.ReadBlockBatchAsync(filePath).ConfigureAwait(false);

            int bufferIndex = 0;

            var blocks = new List<Block>();

            while (bufferIndex < buffer.Length)
            {
              var block = new Block();

              block.HeaderHash =
              SHA256Generator.ComputeHash(
                SHA256Generator.ComputeHash(
                  buffer,
                  bufferIndex,
                  COUNT_HEADER_BYTES));

              ValidateHeaderHash(block.HeaderHash);

              int indexMerkleRoot = bufferIndex + OFFSET_INDEX_MERKLE_ROOT;
              int indexHashPrevious = bufferIndex + OFFSET_INDEX_HASH_PREVIOUS;
              byte[] hashPrevious = new byte[HASH_BYTE_SIZE];
              Array.Copy(buffer, indexHashPrevious, hashPrevious, 0, HASH_BYTE_SIZE);

              bufferIndex += COUNT_HEADER_BYTES;

              int tXCount = (int)VarInt.GetUInt64(buffer, ref bufferIndex);

              block.TXs = new TX[tXCount];

              byte[] merkleRootHash = ComputeMerkleRootHash(
                buffer,
                ref bufferIndex,
                block.TXs,
                SHA256Generator,
                Stopwatch);

              if (!merkleRootHash.IsEqual(buffer, indexMerkleRoot))
              {
                throw new UTXOException("Payload corrupted.");
              }

              blocks.Add(block);
            }
            
            lock (UTXO.MergeLOCK)
            {
              if (UTXO.IndexBatchMerge != BatchIndex)
              {
                UTXO.QueueBlocksMerge.Add(BatchIndex, blocks);
                return;
              }
            }

            while (true)
            {
              UTXO.Merge(blocks);

              string metricsCSV = string.Format("{0},{1},{2},{3},{4},{5}",
                UTXO.IndexBatchMerge,
                DateTimeOffset.UtcNow.ToUnixTimeSeconds() - UTCTimeStartup,
                UTXO.BlockHeight,
                0,
                UTXO.Cache.GetMetricsCSV(),
                Stopwatch.ElapsedMilliseconds / 1000);

              Console.WriteLine(metricsCSV);
              BuildWriter.WriteLine(metricsCSV);

              lock (UTXO.MergeLOCK)
              {
                UTXO.IndexBatchMerge += 1;

                if (UTXO.QueueBlocksMerge.TryGetValue(UTXO.IndexBatchMerge, out blocks))
                {
                  UTXO.QueueBlocksMerge.Remove(UTXO.IndexBatchMerge);
                  continue;
                }

                break;
              }
            }
          }
          else
          {
            UTXO.ParallelBatchesExistInArchive = false;
          }
        }
        catch (Exception ex)
        {
          Console.WriteLine(ex.Message);
        }
      }
      
      void ValidateHeaderHash(byte[] headerHash)
      {
        if (ChainHeader == null)
        {
          ChainHeader = UTXO.Headerchain.ReadHeader(headerHash, SHA256Generator);
        }

        byte[] headerHashValidator;

        if (ChainHeader.HeadersNext == null)
        {
          headerHashValidator = ChainHeader.GetHeaderHash(SHA256Generator);
        }
        else
        {
          ChainHeader = ChainHeader.HeadersNext[0];
          headerHashValidator = ChainHeader.NetworkHeader.HashPrevious;
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
}
