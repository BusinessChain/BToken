using System;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Threading.Tasks.Dataflow;

namespace BToken.Chaining
{
  public partial class Blockchain
  {
    class UTXOArchiveLoader
    {
      const int COUNT_ARCHIVE_PARSER_PARALLEL = 8;

      Blockchain Blockchain;
      readonly object LOCK_BatchIndexLoad = new object();
      public int BatchIndexLoad;
      public int BatchIndexNextOutput;

      readonly object LOCK_IsOutputStageLocked = new object();
      bool IsOutputStageLocked;
      public ChainHeader HeaderPostedToMergerLast;
      Dictionary<int, UTXOBatch> OutputQueue = new Dictionary<int, UTXOBatch>();


      public UTXOArchiveLoader(Blockchain blockchain)
      {
        Blockchain = blockchain;
      }

      public async Task RunAsync()
      {
        BatchIndexLoad = Blockchain.Merger.BatchIndexNext;
        BatchIndexNextOutput = Blockchain.Merger.BatchIndexNext;
        HeaderPostedToMergerLast = Blockchain.Merger.HeaderMergedLast;


        Task[] archiveLoaderTasks = new Task[COUNT_ARCHIVE_PARSER_PARALLEL];
        for (int i = 0; i < COUNT_ARCHIVE_PARSER_PARALLEL; i += 1)
        {
          archiveLoaderTasks[i] = LoadBatchesAsync();
        }

        await Task.WhenAll(archiveLoaderTasks);
      }

      async Task LoadBatchesAsync()
      {
        UTXOParser parser = new UTXOParser(Blockchain);
        
        byte[] batchBuffer;
        int batchIndex;

        while (true)
        {
          lock (LOCK_BatchIndexLoad)
          {
            batchIndex = BatchIndexLoad;
            BatchIndexLoad += 1;
          }

          try
          {
            batchBuffer = await BlockArchiver
              .ReadBlockBatchAsync(batchIndex)
              .ConfigureAwait(false);
          }
          catch (IOException)
          {
            if (batchIndex == 0)
            {
              batchBuffer = Blockchain.GenesisBlock.BlockBytes;
            }
            else
            {
              lock (LOCK_BatchIndexLoad)
              {
                BatchIndexLoad -= 1;
              }

              return;
            }
          }
          catch(Exception ex)
          {
            Console.WriteLine("Exception: '{0}' in BlockArchiver loading batchIndex {1}", 
              ex.Message,
              batchIndex);

            throw ex;
          }

          try
          {
            UTXOBatch batch = parser.ParseBatch(batchBuffer, batchIndex);

            await PostToOutputStage(batch);
          }
          catch (ChainException)
          {
            lock (LOCK_BatchIndexLoad)
            {
              BatchIndexLoad = batchIndex;
            }

            return;
          }
          catch (Exception ex)
          {
            Console.WriteLine("Exception: '{0}' in parser {1}",
              ex.Message,
              parser.GetHashCode());

            throw ex;
          }
        }
        
      }

      public async Task PostToOutputStage(UTXOBatch batch)
      {
        while(true)
        {
          lock(LOCK_IsOutputStageLocked)
          {
            if(!IsOutputStageLocked)
            {
              IsOutputStageLocked = true;
              break;
            }
          }

          await Task.Delay(1000);
        }

        if (batch.BatchIndex != BatchIndexNextOutput)
        {
          if(!OutputQueue.ContainsKey(batch.BatchIndex))
          {
            OutputQueue.Add(batch.BatchIndex, batch);
          }
        }
        else
        {
          while (true)
          {
            if (HeaderPostedToMergerLast != batch.HeaderPrevious)
            {
              var ex = new ChainException(
                string.Format("HeaderPrevious {0} of Batch {1} not equal to \nHeaderMergedLast {2}",
                batch.HeaderPrevious.GetHeaderHash().ToHexString(),
                batch.BatchIndex,
                HeaderPostedToMergerLast.GetHeaderHash().ToHexString()));

              lock (LOCK_IsOutputStageLocked)
              {
                IsOutputStageLocked = false;
              }

              throw ex;
            }

            while (!Blockchain.Merger.Buffer.Post(batch))
            {
              await Task.Delay(1000);
            }

            BatchIndexNextOutput += 1;
            HeaderPostedToMergerLast = batch.HeaderLast;

            if (OutputQueue.TryGetValue(BatchIndexNextOutput, out batch))
            {
              OutputQueue.Remove(BatchIndexNextOutput);
            }
            else
            {
              break;
            }
          }
        }

        lock (LOCK_IsOutputStageLocked)
        {
          IsOutputStageLocked = false;
        }
      }
    }
  }
}
