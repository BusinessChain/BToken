using System;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using System.Threading.Tasks;
using System.Threading.Tasks.Dataflow;

namespace BToken.Chaining
{
  abstract class AbstractGateway
  {
    int CountSessions;

    public AbstractGateway(int countSessions)
    {
      CountSessions = countSessions;
    }



    int BatchIndexLoad;
    protected ItemBatchContainer ContainerInsertedLast;

    public async Task Start()
    {
      StartListener();

      LoadImage(out BatchIndexLoad);

      await SynchronizeWithArchive();
      
      StartBatcherAsync();

      await Synchronize(ContainerInsertedLast);
    }



    protected abstract void LoadImage(out int archiveIndexNext);
    protected abstract Task StartListener();
    


    async Task Synchronize(ItemBatchContainer containerInsertedLast)
    {
      ContainerInsertedLast = containerInsertedLast;

      Task[] syncTasks = new Task[CountSessions];

      for (int i = 0; i < CountSessions; i += 1)
      {
        syncTasks[i] = CreateSyncSessionTask();
      }

      await Task.WhenAll(syncTasks);
    }

    protected abstract Task CreateSyncSessionTask();



    int ContainerhIndexOutputQueue;
    bool InvalidContainerEncountered;
    const int COUNT_ARCHIVE_LOADER_PARALLEL = 8;
    Task[] ArchiveLoaderTasks = new Task[COUNT_ARCHIVE_LOADER_PARALLEL];

    async Task SynchronizeWithArchive()
    {
      ContainerhIndexOutputQueue = BatchIndexLoad;

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
      ItemBatchContainer container;
      int containerIndex;

      do
      {
        lock (LOCK_BatchIndexLoad)
        {
          containerIndex = BatchIndexLoad;
          BatchIndexLoad += 1;
        }

        try
        {
          container = LoadDataContainer(containerIndex);
          container.Parse();
          container.IsValid = true;
        }
        catch(IOException)
        {
          return;
        }
        catch(Exception ex)
        {
          Console.WriteLine("Exception in archive load of batch {0}: {1}",
            containerIndex,
            ex.Message);

          return;
        }

      } while (await SendToOutputQueue(container));
    }

    protected abstract ItemBatchContainer LoadDataContainer(
      int containerIndex);



    Dictionary<int, ItemBatchContainer> OutputQueue = 
      new Dictionary<int, ItemBatchContainer>();

    async Task<bool> SendToOutputQueue(ItemBatchContainer container)
    {
      while (true)
      {
        lock (LOCK_OutputQueue)
        {
          if (InvalidContainerEncountered)
          {
            return false;
          }

          if (container.Index == ContainerhIndexOutputQueue)
          {
            break;
          }

          if (OutputQueue.Count < 10)
          {
            OutputQueue.Add(container.Index, container);
            return true;
          }
        }
        
        await Task.Delay(2000).ConfigureAwait(false);
      }

      while (true)
      {
        if (
          !container.IsValid ||
          !TryInsertContainer(container))
        { 
          InvalidContainerEncountered = true;
          return false;
        }

        ContainerInsertedLast = container;

        lock (LOCK_OutputQueue)
        {
          ContainerhIndexOutputQueue += 1;
          
          if (OutputQueue.TryGetValue(ContainerhIndexOutputQueue, out container))
          {
            OutputQueue.Remove(ContainerhIndexOutputQueue);
          }
          else
          {
            return true;
          }
        }
      }
    }

    protected abstract bool TryInsertContainer(
      ItemBatchContainer container);



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
      OutputBatch = new DataBatch(ContainerhIndexOutputQueue);

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
        if (!TryInsertBatch(OutputBatch, out ItemBatchContainer containerInvalid))
        {
          ReportInvalidBatch(containerInvalid.Batch);
        }

        ArchiveBatch(OutputBatch);

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
              if (!TryInsertBatch(OutputBatch, out ItemBatchContainer containerInvalid))
              {
                ReportInvalidBatch(containerInvalid.Batch);
              }

              ArchiveBatch(OutputBatch);
            }

            return;
          }

          if (OutputBatch.CountItems + FIFOItems.Peek().CountItems > SIZE_OUTPUT_BATCH)
          {
            if (!TryInsertBatch(OutputBatch, out ItemBatchContainer containerInvalid))
            {
              ReportInvalidBatch(containerInvalid.Batch);
            }

            ArchiveBatch(OutputBatch);

            OutputBatch = new DataBatch(OutputBatch.Index + 1);
            break;
          }
        } while (true);
      }
    }

    protected abstract bool TryInsertBatch(
      DataBatch uTXOBatch,
      out ItemBatchContainer containerInvalid);

    protected abstract void ArchiveBatch(DataBatch batch);


    void ReportInvalidBatch(DataBatch batch)
    {
      Console.WriteLine("Invalid batch {0} reported",
        batch.Index);

      throw new NotImplementedException();
    }
  }
}
