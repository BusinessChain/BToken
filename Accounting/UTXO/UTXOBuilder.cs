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

      int BLOCK_BATCH_SIZE = 100;
      int COUNT_TASKS_MAX = 4;
      List<Task<UTXOBatch>> GetBlocksTasks = new List<Task<UTXOBatch>>();
      int NextBatchIndexToMerge;
      List<UTXOBatch> BatchesAwaitingMerge = new List<UTXOBatch>();

      Stopwatch StopWatchMergeBatch = new Stopwatch();
      Stopwatch StopWatchGetBlocks = new Stopwatch();

      public UTXOBuilder(UTXO uTXO, Headerchain.HeaderStream headerStreamer)
      {
        UTXO = uTXO;
        HeaderStreamer = headerStreamer;
        NextBatchIndexToMerge = 0;
      }
      
      public async Task BuildAsync()
      {
        Console.WriteLine(
          "BatchIndex," +
          "PrimaryCacheCompressed," +
          "SecondaryCacheCompressed," +
          "PrimaryCache," +
          "SecondaryCache," +
          "Merge time");

        int batchIndex = 0;
        HeaderLocation[] headerLocations = HeaderStreamer.GetHeaderLocations(BLOCK_BATCH_SIZE);
        
        while (headerLocations != null)
        {
          var batch = new UTXOBatch(batchIndex, headerLocations);
                   
          if (GetBlocksTasks.Count >= COUNT_TASKS_MAX)
          {
            List<UTXOBatch> batchesBlockTaskCompleted = await AwaitBatchesBlockTaskCompleted();
            MergeBatches(batchesBlockTaskCompleted);
          }

          GetBlocksTasks.Add(GetBlocksAsync(batch));
          
          headerLocations = HeaderStreamer.GetHeaderLocations(BLOCK_BATCH_SIZE);
          batchIndex++;
        }

        while(GetBlocksTasks.Any())
        {
          List<UTXOBatch> batchesBlockTaskCompleted = await AwaitBatchesBlockTaskCompleted();
          MergeBatches(batchesBlockTaskCompleted);
        }

        Console.WriteLine("UTXO build complete");
      }
      void MergeBatches(List<UTXOBatch> batches)
      {
        foreach (UTXOBatch batch in batches)
        {
          StopWatchMergeBatch.Restart();

          for (int b = 0; b < BLOCK_BATCH_SIZE; b++)
          {
            Block block = batch.Blocks[b];
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

          StopWatchMergeBatch.Stop();

          Console.WriteLine("{0},{1},{2},{3},{4},{5}",
            batch.BatchIndex,
            UTXO.GetCountPrimaryCacheItemsUInt32(),
            UTXO.GetCountSecondaryCacheItemsUInt32(),
            UTXO.GetCountPrimaryCacheItemsByteArray(),
            UTXO.GetCountSecondaryCacheItemsByteArray(),
            StopWatchMergeBatch.ElapsedMilliseconds);

          NextBatchIndexToMerge++;
        }
      }
      async Task<List<UTXOBatch>> AwaitBatchesBlockTaskCompleted()
      {
        var batches = new List<UTXOBatch>();

        Task<UTXOBatch> getBlockTaskCompleted = await Task.WhenAny(GetBlocksTasks);
        GetBlocksTasks.Remove(getBlockTaskCompleted);
        UTXOBatch batch = await getBlockTaskCompleted;

        if(batch != null)
        {
          BatchesAwaitingMerge.Add(batch);

          int nextBatchIndexToMerge = NextBatchIndexToMerge;
          if (batch.BatchIndex == nextBatchIndexToMerge)
          {
            do
            {
              batches.Add(batch);
              BatchesAwaitingMerge.Remove(batch);
              nextBatchIndexToMerge++;

              batch = BatchesAwaitingMerge.Find(b => b.BatchIndex == nextBatchIndexToMerge);
            } while (batch != null);
          }
        }

        return batches;
      }
      async Task<UTXOBatch> GetBlocksAsync(UTXOBatch batch)
      {
        try
        {
          StopWatchGetBlocks.Restart();

          for (int i = 0; i < BLOCK_BATCH_SIZE; i++)
          {
            batch.Blocks[i] = await UTXO.GetBlockAsync(batch.HeaderLocations[i].Hash);
          }

          StopWatchGetBlocks.Stop();

          return batch;
        }
        catch (UTXOException ex)
        {
          Console.WriteLine("Build batch '{0}' threw UTXOException: '{1}'",
            batch.BatchIndex, ex.Message);

          return null;
        }
        catch (Exception ex)
        {
          Console.WriteLine("Build batch '{0}' threw unexpected exception: '{1}'",
            batch.BatchIndex, ex.Message);

          return null;
        }
      }
      
    }
  }
}
