
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Runtime.Remoting.Metadata.W3cXsd2001;

using BToken.Chaining;
using BToken.Networking;

namespace BToken.Accounting
{
  public partial class UTXO
  {
    partial class UTXOBuilder
    {
      UTXO UTXO;
      Headerchain.HeaderStream HeaderStreamer;

      int BATCH_SIZE = 100;
      int COUNT_TASKS_MAX = 4;
      List<Task<UTXOBatch>> GetBlocksTasks = new List<Task<UTXOBatch>>();
      int NextBatchIndexToMerge = 0;
      List<UTXOBatch> BatchesAwaitingMerge = new List<UTXOBatch>();


      public UTXOBuilder(UTXO uTXO, Headerchain.HeaderStream headerStreamer)
      {
        UTXO = uTXO;
        HeaderStreamer = headerStreamer;
      }
      
      public async Task BuildAsync()
      {
        int batchIndex = 0;
        HeaderLocation[] headerLocations = HeaderStreamer.GetHeaderLocations(BATCH_SIZE);

        while (headerLocations != null)
        {
          var batch = new UTXOBatch(batchIndex, headerLocations);
                   
          if (GetBlocksTasks.Count >= COUNT_TASKS_MAX)
          {
            await BuildBatchAsync();
          }

          GetBlocksTasks.Add(GetBlocksAsync(batch));
          
          headerLocations = HeaderStreamer.GetHeaderLocations(BATCH_SIZE);
          batchIndex++;
        }

        while(GetBlocksTasks.Any())
        {
          await BuildBatchAsync();
        }

        Console.WriteLine("UTXO build complete");
      }
      async Task BuildBatchAsync()
      { 
        UTXOBatch batch = await AwaitNextBatchBlockTaskCompleted();

        while (batch != null)
        {
          if (batch.BatchIndex == NextBatchIndexToMerge)
          {
            MergeBatch(batch);
            
            BatchesAwaitingMerge.Remove(batch);

            NextBatchIndexToMerge++;
            batch = BatchesAwaitingMerge.Find(b => b.BatchIndex == NextBatchIndexToMerge);
          }
          else
          {
            BatchesAwaitingMerge.Add(batch);
            batch = await AwaitNextBatchBlockTaskCompleted();
          }
        }
      }
      async Task<UTXOBatch> AwaitNextBatchBlockTaskCompleted()
      {
        Task<UTXOBatch> getBlockTaskCompleted = await Task.WhenAny(GetBlocksTasks);
        GetBlocksTasks.Remove(getBlockTaskCompleted);
        return await getBlockTaskCompleted;
      }
      async Task<UTXOBatch> GetBlocksAsync(UTXOBatch batch)
      {
        try
        {
          for(int i = 0; i < BATCH_SIZE; i++)
          {
            batch.Blocks[i] = await UTXO.GetBlockAsync(batch.HeaderLocations[i].Hash);
          }

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
      
      void MergeBatch(UTXOBatch batch)
      {
        foreach (Block block in batch.Blocks)
        {
          List<TX> tXs = block.TXs;
          List<byte[]> tXHashes = block.TXHashes;
          UInt256 headerHash = block.HeaderHash;

          for (int t = 0; t < tXs.Count; t++)
          {
            byte[] uTXO = CreateUTXO(headerHash, tXs[t].Outputs.Count);
            UTXO.Write(tXHashes[t], uTXO);
          }

          for (int t = 1; t < tXs.Count; t++)
          {
            for (int i = 0; i < tXs[t].Inputs.Count; i++)
            {
              UTXO.SpendUTXO(tXs[t].Inputs[i]);
            }
          }
        }

        Console.WriteLine("{0},{1},{2}",
          batch.BatchIndex,
          UTXO.PrimaryCache.Count,
          UTXO.SecondaryCache.Count);
      }

    }
  }
}
