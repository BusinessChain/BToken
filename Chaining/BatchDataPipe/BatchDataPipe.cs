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

    public BatchDataPipe(IDatabase database)
    {
      Database = database;
    }



    const int COUNT_NETWORK_PARSER_PARALLEL = 4;
    const int COUNT_ARCHIVE_LOADER_PARALLEL = 4;
    Task[] ArchiveLoaderTasks = new Task[COUNT_ARCHIVE_LOADER_PARALLEL];
    public int BatchIndexLoad;
    int BatchIndexOutputQueue;

    public async Task Start()
    {
      BatchIndexLoad = Database.LoadImage();
      BatchIndexOutputQueue = BatchIndexLoad;

      Parallel.For(
        0,
        COUNT_ARCHIVE_LOADER_PARALLEL,
        i => ArchiveLoaderTasks[i] = StartArchiveLoaderAsync());

      await Task.WhenAll(ArchiveLoaderTasks);

      for (int i = 0; i < COUNT_NETWORK_PARSER_PARALLEL; i += 1)
      {
        StartParserAsync();
      }

      StartBatcherAsync();
    }



    const int SIZE_OUTPUT_BATCH = 100000;
    public BufferBlock<DataBatch> InputBuffer =
      new BufferBlock<DataBatch>(new DataflowBlockOptions { BoundedCapacity = 10 });
    int InputBatchIndex;
    Dictionary<int, DataBatch> QueueDownloadBatch = new Dictionary<int, DataBatch>();
    Queue<ItemBatchContainer> FIFOItems = new Queue<ItemBatchContainer>();
    int ItemCountFIFO;
    DataBatch InputBatch;
    DataBatch OutputBatch;

    async Task StartBatcherAsync()
    {
      OutputBatch = Database.CreateBatch();

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

          await DequeueOutputBatches();

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

    async Task DequeueOutputBatches()
    {
      if (OutputBatch.CountItems + FIFOItems.Peek().CountItems > SIZE_OUTPUT_BATCH)
      {
        await ParserBuffer.SendAsync(OutputBatch);
        OutputBatch = Database.CreateBatch();
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
            if (InputBatch.IsLastBatch)
            {
              await ParserBuffer.SendAsync(OutputBatch);
            }

            return;
          }

          if (OutputBatch.CountItems + FIFOItems.Peek().CountItems > SIZE_OUTPUT_BATCH)
          {
            await ParserBuffer.SendAsync(OutputBatch);
            OutputBatch = Database.CreateBatch();
            break;
          }
        } while (true);
      }
    }



    BufferBlock<DataBatch> ParserBuffer =
      new BufferBlock<DataBatch>(new DataflowBlockOptions { BoundedCapacity = 10 });
    readonly object LOCK_OutputStage = new object();
    Dictionary<int, DataBatch> OutputQueue = new Dictionary<int, DataBatch>();

    async Task StartParserAsync()
    {
      while (true)
      {
        DataBatch batch = await ParserBuffer.ReceiveAsync()
          .ConfigureAwait(false);

        batch.Parse();

        Task archiveBatchTask = Database.ArchiveBatchAsync(batch);

        await SendToOutputQueue(batch);
      }
    }
        


    readonly object LOCK_BatchIndexLoad = new object();
    readonly object LOCK_OutputQueue = new object();
    readonly object LOCK_OccurredExceptionInAnyLoader = new object();
    bool OccurredExceptionInAnyLoader;

    async Task StartArchiveLoaderAsync()
    {
      int batchIndex;
      while (true)
      {
        lock(LOCK_OccurredExceptionInAnyLoader)
        {
          if (OccurredExceptionInAnyLoader)
          {
            return;
          }
        }

        lock (LOCK_BatchIndexLoad)
        {
          batchIndex = BatchIndexLoad;
          BatchIndexLoad += 1;
        }

        try
        {
          DataBatch batch = Database.LoadBatchFromArchive(batchIndex);
          await SendToOutputQueue(batch);
        }
        catch (Exception ex)
        {
          Console.WriteLine("Exception: '{0}' in data pipe with batch {1}",
            ex.Message,
            batchIndex);

          lock (LOCK_BatchIndexLoad)
          {
            if(OccurredExceptionInAnyLoader)
            {
              return;
            }

            OccurredExceptionInAnyLoader = true;
            BatchIndexLoad = batchIndex;
          }

          return;
        }
      }
    }



    async Task SendToOutputQueue(DataBatch batch)
    {
      while (true)
      {
        if (OutputQueue.Count < 10)
        {
          break;
        }

        await Task.Delay(2000).ConfigureAwait(false);
      }

      lock (LOCK_OutputQueue)
      {
        if (batch.Index != BatchIndexOutputQueue)
        {
          OutputQueue.Add(batch.Index, batch);

          return;
        }
      }

      while (true)
      {
        Database.InsertBatch(batch);

        lock (LOCK_OutputQueue)
        {
          BatchIndexOutputQueue += 1;
          
          if (OutputQueue.TryGetValue(BatchIndexOutputQueue, out batch))
          {
            OutputQueue.Remove(BatchIndexOutputQueue);
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
