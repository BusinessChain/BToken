using System;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using System.Threading.Tasks;
using System.Threading.Tasks.Dataflow;

namespace BToken.Chaining
{
  abstract class DataSynchronizer
  {
    int CountSessions;

    public DataSynchronizer(int countSessions)
    {
      CountSessions = countSessions;
    }



    int ArchiveLoadIndex;

    public async Task Start()
    {
      LoadImage(out ArchiveLoadIndex);

      await SynchronizeWithArchive();
      
      StartInputBatchBuffer();

      await SynchronizeWithNetwork();
    }



    protected abstract void LoadImage(out int archiveIndexNext);
    


    async Task SynchronizeWithNetwork()
    {
      Task[] syncTasks = new Task[CountSessions];

      for (int i = 0; i < CountSessions; i += 1)
      {
        syncTasks[i] = CreateSyncSessionTask();
      }

      await Task.WhenAll(syncTasks);
    }

    protected abstract Task CreateSyncSessionTask();



    int ContainerIndexOutputQueue;
    bool InvalidContainerEncountered;
    const int COUNT_ARCHIVE_LOADER_PARALLEL = 8;
    Task[] ArchiveLoaderTasks = new Task[COUNT_ARCHIVE_LOADER_PARALLEL];

    async Task SynchronizeWithArchive()
    {
      Console.WriteLine("synchronize {0} with archive", GetType().Name);

      ContainerIndexOutputQueue = ArchiveLoadIndex;

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
      DataBatchContainer container;
      int containerIndex;

      do
      {
        lock (LOCK_BatchIndexLoad)
        {
          containerIndex = ArchiveLoadIndex;
          ArchiveLoadIndex += 1;
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
          Console.WriteLine(
            "Exception loading archive {0}: {1}",
            containerIndex,
            ex.Message);

          return;
        }

      } while (await SendToOutputQueue(container));
    }

    protected abstract DataBatchContainer LoadDataContainer(
      int containerIndex);



    Dictionary<int, DataBatchContainer> OutputQueue = 
      new Dictionary<int, DataBatchContainer>();

    async Task<bool> SendToOutputQueue(
      DataBatchContainer container)
    {
      while (true)
      {
        lock (LOCK_OutputQueue)
        {
          if (InvalidContainerEncountered)
          {
            return false;
          }

          if (container.Index == ContainerIndexOutputQueue)
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

        lock (LOCK_OutputQueue)
        {
          ContainerIndexOutputQueue += 1;
          
          if (OutputQueue.TryGetValue(ContainerIndexOutputQueue, out container))
          {
            OutputQueue.Remove(ContainerIndexOutputQueue);
          }
          else
          {
            return true;
          }
        }
      }
    }

    protected abstract bool TryInsertContainer(
      DataBatchContainer container);


    const int SIZE_OUTPUT_BATCH = 50000;

    public BufferBlock<DataBatch> InputBuffer = 
      new BufferBlock<DataBatch>(
        new DataflowBlockOptions { BoundedCapacity = 10 });
    int BatchIndex;
    DataBatch Batch;

    Dictionary<int, DataBatch> QueueDownloadBatch = 
      new Dictionary<int, DataBatch>();

    async Task StartInputBatchBuffer()
    {
      while (true)
      {
        Batch = await InputBuffer.ReceiveAsync().ConfigureAwait(false);

        if (Batch.Index != BatchIndex)
        {
          QueueDownloadBatch.Add(Batch.Index, Batch);
          continue;
        }

        do
        {
          TryInsertBatch(Batch);

          BatchIndex += 1;

          if (QueueDownloadBatch.TryGetValue(
            BatchIndex, 
            out Batch))
          {
            QueueDownloadBatch.Remove(BatchIndex);
          }
          else
          {
            break;
          }
        } while (true);
      }
    }


    protected abstract bool TryInsertBatch(DataBatch batch);


    void ReportInvalidBatch(DataBatch batch)
    {
      Console.WriteLine("Invalid batch {0} reported",
        batch.Index);

      throw new NotImplementedException();
    }
  }
}
