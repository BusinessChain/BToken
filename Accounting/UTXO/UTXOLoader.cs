using System;
using System.Diagnostics;
using System.IO;
using System.Text;
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
      const int OFFSET_INDEX_MERKLE_ROOT = 36;
      UTXO UTXO;

      int BatchIndex;
      int QueueIndex;
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
        QueueIndex = queueIndex;
        BuildWriter = buildWriter;

        Stopwatch = stopwatch;
      }

      public async Task LoadAsync()
      {
        try
        {
          if (BlockArchiver.Exists(BatchIndex, out string filePath))
          {
            byte[] blockBatchBytes = await BlockArchiver.ReadBlockBatchAsync(filePath);

            lock (UTXO.MergeLOCK)
            {
              if (UTXO.MergeBatchIndex != BatchIndex)
              {
                UTXO.QueueMergeBlockBatches[QueueIndex] = blockBatchBytes;
                return;
              }
            }

            while (true)
            {
              Merge(blockBatchBytes);

              string metricsCSV = string.Format("{0},{1},{2},{3},{4},{5}",
                UTXO.MergeBatchIndex,
                DateTimeOffset.UtcNow.ToUnixTimeSeconds() - UTCTimeStartup,
                UTXO.BlockHeight,
                0,
                UTXO.Cache.GetMetricsCSV(),
                Stopwatch.ElapsedMilliseconds / 1000);

              Console.WriteLine(metricsCSV);
              BuildWriter.WriteLine(metricsCSV);

              lock (UTXO.MergeLOCK)
              {
                UTXO.MergeBatchIndex++;
                QueueIndex++;

                if (QueueIndex == COUNT_BATCHES_PARALLEL ||
                  UTXO.QueueMergeBlockBatches[QueueIndex] == null)
                {
                  return;
                }

                blockBatchBytes = UTXO.QueueMergeBlockBatches[QueueIndex];
                UTXO.QueueMergeBlockBatches[QueueIndex] = null;
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

      void Merge(byte[] buffer)
      {
        int bufferIndex = 0;

        while (bufferIndex < buffer.Length)
        {
          byte[] headerHash =
          SHA256Generator.ComputeHash(
            SHA256Generator.ComputeHash(
              buffer,
              bufferIndex,
              COUNT_HEADER_BYTES));

          ValidateHeaderHash(headerHash);

          int indexMerkleRoot = bufferIndex + OFFSET_INDEX_MERKLE_ROOT;
          bufferIndex += COUNT_HEADER_BYTES;

          int tXCount = (int)VarInt.GetUInt64(buffer, ref bufferIndex);
          
          var tXs = new TX[tXCount];

          byte[] merkleRootHash = ComputeMerkleRootHash(
            buffer,
            ref bufferIndex,
            tXs,
            SHA256Generator,
            Stopwatch);

          if (!merkleRootHash.IsEqual(buffer, indexMerkleRoot))
          {
            throw new UTXOException("Payload corrupted.");
          }

          UTXO.Merge(
            buffer,
            tXs,
            headerHash);
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
