using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks.Dataflow;
using System.Threading.Tasks;

namespace BToken.Accounting
{
  public partial class UTXO
  {
    class ArchiveUTXOBatchLoader
    {
      UTXO UTXO;
      const int COUNT_BATCHES_PARALLEL = 8;

      public byte[] HeaderHashSentToMergerLast = new byte[COUNT_HEADER_BYTES];
      readonly object LOCK_BatchFileIndex = new object();
      int BatchFileIndex;
      readonly object LOCK_BatchIndexMerge = new object();
      int BatchIndexMerge;


      public ArchiveUTXOBatchLoader(UTXO uTXO)
      {
        UTXO = uTXO;
      }

      public async Task<byte[]> RunAsync()
      {
        BatchFileIndex = UTXO.BatchIndexNextMerger;
        BatchIndexMerge = UTXO.BatchIndexNextMerger;
        
        Task[] archiveLoaderTasks = new Task[COUNT_BATCHES_PARALLEL];
        for (int i = 0; i < COUNT_BATCHES_PARALLEL; i += 1)
        {
          archiveLoaderTasks[i] = LoadBatchesFromArchiveAsync();
        }
        await Task.WhenAll(archiveLoaderTasks);

        return HeaderHashSentToMergerLast;
      }

      async Task LoadBatchesFromArchiveAsync()
      {
        int batchIndex;

        try
        {
          while (true)
          {
            lock (LOCK_BatchFileIndex)
            {
              batchIndex = BatchFileIndex;
              BatchFileIndex += 1;
            }

            if (!BlockArchiver.Exists(batchIndex, out string filePath))
            {
              return;
            }

            UTXOBatch batch = new UTXOBatch()
            {
              BatchIndex = batchIndex,
              Buffer = await BlockArchiver.ReadBlockBatchAsync(filePath).ConfigureAwait(false)
            };

            UTXO.Parser.ParseBatch(batch);

            lock (LOCK_BatchIndexMerge)
            {
              UTXO.Merger.BatchBuffer.Post(batch);
              BatchIndexMerge += 1;
              HeaderHashSentToMergerLast = batch.Blocks.Last().HeaderHash;
            }
          }
        }
        catch (Exception ex)
        {
          Console.WriteLine(ex.Message);
          throw ex;
        }
      }
    }
  }
}
