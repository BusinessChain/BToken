using System;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Threading.Tasks.Dataflow;

using BToken.Chaining;

namespace BToken.Accounting
{
  public partial class UTXO
  {
    class UTXOArchiveLoader
    {
      const int COUNT_ARCHIVE_PARSER_PARALLEL = 8;

      UTXO UTXO;
      CancellationTokenSource CancellationLoader = new CancellationTokenSource();
      readonly object LOCK_BatchIndexLoad = new object();
      public int BatchIndexLoad;
      public int BatchIndexNextOutput;

      readonly object LOCK_OutputStage = new object();
      public Headerchain.ChainHeader HeaderPostedToMergerLast;
      Dictionary<int, UTXOBatch> OutputQueue = new Dictionary<int, UTXOBatch>();


      public UTXOArchiveLoader(UTXO uTXO)
      {
        UTXO = uTXO;
        BatchIndexLoad = UTXO.Merger.BatchIndexNext;
        BatchIndexNextOutput = UTXO.Merger.BatchIndexNext;
        HeaderPostedToMergerLast = UTXO.Merger.HeaderMergedLast;
      }

      public async Task RunAsync()
      {
        Task[] archiveLoaderTasks = new Task[COUNT_ARCHIVE_PARSER_PARALLEL];
        for (int i = 0; i < COUNT_ARCHIVE_PARSER_PARALLEL; i += 1)
        {
          archiveLoaderTasks[i] = LoadBatchesAsync();
        }

        await Task.WhenAll(archiveLoaderTasks);
      }

      async Task LoadBatchesAsync()
      {
        UTXOParser parser = new UTXOParser(UTXO);
        
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
              batchBuffer = UTXO.GenesisBlock.BlockBytes;
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

          try
          {
            UTXOBatch batch = parser.ParseBatch(batchBuffer, batchIndex);
            
            PostToOutputBuffer(batch);
          }
          catch (UTXOException)
          {
            lock (LOCK_BatchIndexLoad)
            {
              BatchIndexLoad = batchIndex;
            }

            return;
          }
        }
      }

      public void PostToOutputBuffer(UTXOBatch batch)
      {
        lock (LOCK_OutputStage)
        {
          if (batch.BatchIndex != BatchIndexNextOutput)
          {
            OutputQueue.Add(batch.BatchIndex, batch);
          }
          else
          {
            while (true)
            {
              if (HeaderPostedToMergerLast != batch.HeaderPrevious)
              {
                throw new UTXOException(
                  string.Format("HeaderPrevious {0} of Batch {1} not equal to \nHeaderMergedLast {2}",
                  batch.HeaderPrevious.GetHeaderHash().ToHexString(),
                  batch.BatchIndex,
                  HeaderPostedToMergerLast.GetHeaderHash().ToHexString()));
              }

              UTXO.Merger.Buffer.Post(batch);

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
        }
      }
    }
  }
}
