using System.Diagnostics;

using System;
using System.Collections.Generic;
using System.Runtime.Remoting.Metadata.W3cXsd2001;
using System.Linq;
using System.Threading.Tasks;

using BToken.Chaining;

namespace BToken.Accounting
{
  public partial class UTXO
  {
    partial class UTXOBuilder
    {
      UTXO UTXO;
      Headerchain.HeaderStream HeaderStreamer;

      const int SIZE_BATCH_BLOCKS = 8;
      const int COUNT_BATCHES_PARALLEL = 4;


      Block[][] QueueMergeBlocks = new Block[COUNT_BATCHES_PARALLEL][];
      readonly object BatchIndexLOCK = new object();
      int BatchIndex = 0;

      Stopwatch StopWatchBuild= new Stopwatch();
      Stopwatch StopWatchMergeBatch = new Stopwatch();
      Stopwatch StopWatchGetBlocks = new Stopwatch();

      public UTXOBuilder(UTXO uTXO, Headerchain.HeaderStream headerStreamer)
      {
        UTXO = uTXO;
        HeaderStreamer = headerStreamer;
      }

      public async Task BuildAsync()
      {
        StopWatchBuild.Start();

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
          // immer 10000 header in array laden
          for (int i = 0; i < COUNT_BATCHES_PARALLEL; i++)
          {
            headerHashesBatches[i] = HeaderStreamer.GetHeaderLocations(SIZE_BATCH_BLOCKS)
              .Select(h => h.Hash)
              .ToArray();
          }

          Console.WriteLine("Start merging blocks for batch '{0}'", batchIndexOffset);
          StopWatchGetBlocks.Restart();

          Parallel.For(0, COUNT_BATCHES_PARALLEL,
            async i => {
              if (headerHashesBatches[i] != null)
              {
                //Block[] blocks = await UTXO.GetBlockBatchAsync(
                //  batchIndexOffset + i,
                //  headerHashesBatches[i]);

                //Validieren

                // In HeaderArray suchen und Hash abgleichen
                
                //MergeBatch(i, blocks);
              }
            });

          StopWatchGetBlocks.Stop();
          Console.WriteLine("Finished merging blocks for batchOffset '{0}', time: '{1}'",
            batchIndexOffset,
            StopWatchGetBlocks.Elapsed);

          batchIndexOffset += COUNT_BATCHES_PARALLEL;
        } while (!headerHashesBatches.Any(h => h == null));

      }
      void MergeBatch(int batchIndex, Block[] blocks)
      {
        //lock (BatchIndexLOCK)
        //{
        //  QueueMergeBlocks[batchIndex] = blocks;

        //  if (BatchIndex != batchIndex)
        //  {
        //    return;
        //  }
        //}

        //while (true)
        //{
        //  StopWatchMergeBatch.Restart();

        //  for (int b = 0; b < SIZE_BATCH_BLOCKS; b++)
        //  {
        //    Block block = QueueMergeBlocks[BatchIndex][b];
        //    List<TX> tXs = block.TXs;
        //    List<byte[]> tXHashes = block.TXHashes;
        //    byte[] headerHashBytes = block.HeaderHash.GetBytes();

        //    for (int t = 0; t < tXs.Count; t++)
        //    {
        //      // debug

        //      byte[] outputTXHash = new byte[tXHashes[t].Length];
        //      tXHashes[t].CopyTo(outputTXHash, 0);
        //      Array.Reverse(outputTXHash);
        //      if (new SoapHexBinary(outputTXHash).ToString() == "C02D4826DEE0F0A810E9DC3DB49A484CDF90832C56991F0EBA88418B80C7EC29")
        //      {
        //        byte[] inputTXHash = new byte[tXHashes[t].Length];
        //        tXHashes[t].CopyTo(inputTXHash, 0);
        //        Array.Reverse(inputTXHash);

        //        Console.WriteLine("Write outputs of TX '{0}' to UTXO",
        //          new SoapHexBinary(outputTXHash));
        //      }

        //      // end debug

        //      UTXO.InsertUTXO(tXHashes[t], headerHashBytes, tXs[t].Outputs.Count);
        //    }

        //    for (int t = 1; t < tXs.Count; t++)
        //    {
        //      for (int i = 0; i < tXs[t].Inputs.Count; i++)
        //      {
        //        try
        //        {
        //          // debug

        //          byte[] outputTXHash = new byte[tXHashes[t].Length];
        //          tXs[t].Inputs[i].TXIDOutput.CopyTo(outputTXHash, 0);
        //          Array.Reverse(outputTXHash);
        //          string outputTXHashString = new SoapHexBinary(outputTXHash).ToString();

        //          if (outputTXHashString == "C02D4826DEE0F0A810E9DC3DB49A484CDF90832C56991F0EBA88418B80C7EC29")
        //          {
        //            byte[] inputTXHash = new byte[tXHashes[t].Length];
        //            tXHashes[t].CopyTo(inputTXHash, 0);
        //            Array.Reverse(inputTXHash);

        //            Console.WriteLine("Input '{0}' in TX '{1}' \n attempts to spend " +
        //              "output '{2}' in TX '{3}'.",
        //              i,
        //              new SoapHexBinary(inputTXHash),
        //              tXs[t].Inputs[i].IndexOutput,
        //              new SoapHexBinary(outputTXHash));
        //          }

        //          // end debug


        //          UTXO.SpendUTXO(
        //            tXs[t].Inputs[i].TXIDOutput,
        //            tXs[t].Inputs[i].IndexOutput);
        //        }
        //        catch (UTXOException ex)
        //        {
        //          byte[] inputTXHash = new byte[tXHashes[t].Length];
        //          tXHashes[t].CopyTo(inputTXHash, 0);
        //          Array.Reverse(inputTXHash);

        //          byte[] outputTXHash = new byte[tXHashes[t].Length];
        //          tXs[t].Inputs[i].TXIDOutput.CopyTo(outputTXHash, 0);
        //          Array.Reverse(outputTXHash);

        //          Console.WriteLine("Input '{0}' in TX '{1}' \n failed to spend " +
        //            "output '{2}' in TX '{3}': \n'{4}'.",
        //            i,
        //            new SoapHexBinary(inputTXHash),
        //            tXs[t].Inputs[i].IndexOutput,
        //            new SoapHexBinary(outputTXHash),
        //            ex.Message);
        //        }
        //      }
        //    }
        //  }

        //  StopWatchMergeBatch.Stop();

        //  QueueMergeBlocks[BatchIndex] = null;

        //  lock (BatchIndexLOCK)
        //  {
        //    BatchIndex = (BatchIndex + 1) % COUNT_BATCHES_PARALLEL;

        //    if (QueueMergeBlocks[BatchIndex] == null)
        //    {
        //      return;
        //    }
        //  }
        //}
      }

    }
  }
}
