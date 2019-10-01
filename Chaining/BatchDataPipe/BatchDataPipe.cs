using System;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using System.Threading.Tasks;
using System.Threading.Tasks.Dataflow;

namespace BToken.Chaining
{
  class BatchDataPipe
  {
    IDatabase Database;
    IGateway Gateway;

    public BatchDataPipe(IDatabase database, IGateway gateway)
    {
      Database = database;
      Gateway = gateway;
    }



    int BatchIndexLoad;

    public async Task Start()
    {
      Database.LoadImage(out BatchIndexLoad);

      await SynchronizeWithArchive();
      
      StartBatcherAsync();

      await Gateway.Synchronize(BatchInsertedLast);

      Gateway.StartListener();
    }



    int BatchIndexOutputQueue;
    bool InvalidBatchEncountered;
    const int COUNT_ARCHIVE_LOADER_PARALLEL = 8;
    Task[] ArchiveLoaderTasks = new Task[COUNT_ARCHIVE_LOADER_PARALLEL];

    async Task SynchronizeWithArchive()
    {
      BatchIndexOutputQueue = BatchIndexLoad;

      Parallel.For(
        0,
        COUNT_ARCHIVE_LOADER_PARALLEL,
        i => ArchiveLoaderTasks[i] = StartArchiveLoaderAsync());

      await Task.WhenAll(ArchiveLoaderTasks);
    }

      

    readonly object LOCK_BatchIndexLoad = new object();
    readonly object LOCK_OutputQueue = new object();
    readonly object LOCK_OccurredExceptionInAnyLoader = new object();

    async Task StartArchiveLoaderAsync()
    {
      DataBatch batch;
      int batchIndex;

      do
      {
        lock (LOCK_BatchIndexLoad)
        {
          batchIndex = BatchIndexLoad;
          BatchIndexLoad += 1;
        }

        try
        {
          batch = Database.LoadDataBatch(batchIndex);
          batch.Parse();
          batch.IsValid = true;
        }
        catch(IOException)
        {
          return;
        }
        catch(Exception ex)
        {
          Console.WriteLine("Exception in archive load of batch {0}: {1}",
            batchIndex,
            ex.Message);

          return;
        }

      } while (await SendToOutputQueue(batch));
    }



    Dictionary<int, DataBatch> OutputQueue = new Dictionary<int, DataBatch>();
    DataBatch BatchInsertedLast;

    async Task<bool> SendToOutputQueue(DataBatch batch)
    {
      while (true)
      {
        lock (LOCK_OutputQueue)
        {
          if (InvalidBatchEncountered)
          {
            return false;
          }

          if (batch.Index == BatchIndexOutputQueue)
          {
            break;
          }

          if (OutputQueue.Count < 10)
          {
            OutputQueue.Add(batch.Index, batch);
            return true;
          }
        }
        
        await Task.Delay(2000).ConfigureAwait(false);
      }

      while (true)
      {
        if(
          !batch.IsValid ||
          !Database.TryInsertBatch(batch, out ItemBatchContainer containerInvalid))
        {          
          InvalidBatchEncountered = true;
          return false;
        }

        BatchInsertedLast = batch;

        lock (LOCK_OutputQueue)
        {
          BatchIndexOutputQueue += 1;
          
          if (OutputQueue.TryGetValue(BatchIndexOutputQueue, out batch))
          {
            OutputQueue.Remove(BatchIndexOutputQueue);
          }
          else
          {
            return true;
          }
        }
      }
    }



    const int SIZE_OUTPUT_BATCH = 50000;
    public BufferBlock<DataBatch> InputBuffer = new BufferBlock<DataBatch>(
      new DataflowBlockOptions { BoundedCapacity = 10 });
    int InputBatchIndex;
    Dictionary<int, DataBatch> QueueDownloadBatch = new Dictionary<int, DataBatch>();
    Queue<ItemBatchContainer> FIFOItems = new Queue<ItemBatchContainer>();
    int ItemCountFIFO;
    DataBatch InputBatch;
    DataBatch OutputBatch;

    async Task StartBatcherAsync()
    {
      OutputBatch = new DataBatch(BatchIndexOutputQueue);

      while (true)
      {
        InputBatch = await InputBuffer.ReceiveAsync().ConfigureAwait(false);

        if (InputBatch.Index != InputBatchIndex)
        {
          QueueDownloadBatch.Add(InputBatch.Index, InputBatch);
          continue;
        }

        do
        {
          foreach (ItemBatchContainer batchItemContainer in InputBatch.ItemBatchContainers)
          {
            if (batchItemContainer.CountItems > SIZE_OUTPUT_BATCH)
            {
              throw new InvalidOperationException(
                string.Format("Container {0} of InputBatch {1}, index {2} has more items {3} than SIZE_OUTPUT_BATCH = {4}",
                InputBatch.ItemBatchContainers.FindIndex(b => b == batchItemContainer),
                InputBatch.ToString().Split('.').Last(),
                InputBatch.Index,
                batchItemContainer.CountItems,
                SIZE_OUTPUT_BATCH));
            }

            FIFOItems.Enqueue(batchItemContainer);
            ItemCountFIFO += batchItemContainer.CountItems;
          }

          DequeueOutputBatches();

          InputBatchIndex += 1;

          if (QueueDownloadBatch.TryGetValue(InputBatchIndex, out InputBatch))
          {
            QueueDownloadBatch.Remove(InputBatchIndex);
          }
          else
          {
            break;
          }
        } while (true);
      }
    }

    void DequeueOutputBatches()
    {
      if (OutputBatch.CountItems + FIFOItems.Peek().CountItems > SIZE_OUTPUT_BATCH)
      {
        if (!Database.TryInsertBatch(OutputBatch, out ItemBatchContainer containerInvalid))
        {
          Gateway.ReportInvalidBatch(containerInvalid.Batch);
        }

        Task archiveBatchTask = Database.ArchiveBatch(OutputBatch);

        OutputBatch = new DataBatch(OutputBatch.Index + 1);
      }

      while (true)
      {
        do
        {
          ItemBatchContainer itemContainer = FIFOItems.Dequeue();
          OutputBatch.ItemBatchContainers.Add(itemContainer);
          OutputBatch.CountItems += itemContainer.CountItems;
          ItemCountFIFO -= itemContainer.CountItems;

          if (FIFOItems.Count == 0)
          {
            if (InputBatch.IsFinalBatch)
            {
              if (!Database.TryInsertBatch(OutputBatch, out ItemBatchContainer containerInvalid))
              {
                Gateway.ReportInvalidBatch(containerInvalid.Batch);
              }

              Task archiveBatchTask = Database.ArchiveBatch(OutputBatch);
            }

            return;
          }

          if (OutputBatch.CountItems + FIFOItems.Peek().CountItems > SIZE_OUTPUT_BATCH)
          {
            if (!Database.TryInsertBatch(OutputBatch, out ItemBatchContainer containerInvalid))
            {
              Gateway.ReportInvalidBatch(containerInvalid.Batch);
            }

            Task archiveBatchTask = Database.ArchiveBatch(OutputBatch);

            OutputBatch = new DataBatch(OutputBatch.Index + 1);
            break;
          }
        } while (true);
      }
    }
  }
}
