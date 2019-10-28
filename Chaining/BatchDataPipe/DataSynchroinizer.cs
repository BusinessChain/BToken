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
    int ArchiveIndexLoad;
    protected DirectoryInfo ArchiveDirectory;



    public async Task Start()
    {
      LoadImage(out int archiveIndex);

      ArchiveIndexLoad = archiveIndex + 1;

      await SynchronizeWithArchive();
      
      StartInputBatchBuffer();

      await Task.WhenAll(StartSyncSessionTasks());
    }

    protected abstract void LoadImage(
      out int archiveIndexNext);

    protected abstract Task[] StartSyncSessionTasks();



    int ArchiveIndexStore;
    const int COUNT_ARCHIVE_LOADER_PARALLEL = 8;
    Task[] ArchiveLoaderTasks = new Task[COUNT_ARCHIVE_LOADER_PARALLEL];

    async Task SynchronizeWithArchive()
    {
      Console.WriteLine("synchronize {0} with archive", 
        GetType().Name);

      ArchiveIndexStore = ArchiveIndexLoad;

      Parallel.For(
        0,
        COUNT_ARCHIVE_LOADER_PARALLEL,
        i => ArchiveLoaderTasks[i] = StartArchiveLoaderAsync());

      await Task.WhenAll(ArchiveLoaderTasks);
    }

      

    readonly object LOCK_ArchiveLoadIndex = new object();
    readonly object LOCK_OutputQueue = new object();
    
    async Task StartArchiveLoaderAsync()
    {
      DataContainer container;
      int archiveLoadIndex;

      do
      {
        lock (LOCK_ArchiveLoadIndex)
        {
          archiveLoadIndex = ArchiveIndexLoad;
          ArchiveIndexLoad += 1;
        }

        container = CreateContainer(archiveLoadIndex);

        try
        {
          container.Buffer = File.ReadAllBytes(
            Path.Combine(
              ArchiveDirectory.FullName,
              container.Index.ToString()));

          container.TryParse();
        }
        catch (IOException)
        {
          container.IsValid = false;
        }

      } while (await SendContainerToOutputQueueAndContinue(container));
    }
    
    protected abstract DataContainer CreateContainer(
      int archiveLoadIndex);



    List<DataContainer> Containers = new List<DataContainer>();
    int CountItems;
    Dictionary<int, DataContainer> OutputQueue = 
      new Dictionary<int, DataContainer>();
    bool IsArchiveLoadCompleted;

    async Task<bool> SendContainerToOutputQueueAndContinue(
      DataContainer container)
    {
      while (true)
      {
        if (IsArchiveLoadCompleted)
        {
          return false;
        }

        lock (LOCK_OutputQueue)
        {
          if (container.Index == ArchiveIndexStore)
          {
            break;
          }

          if (OutputQueue.Count < 10)
          {
            OutputQueue.Add(container.Index, container);
            return container.IsValid;
          }
        }
        
        await Task.Delay(2000).ConfigureAwait(false);
      }

      while (
        container.IsValid &&
        TryInsertContainer(container))
      {
        if (CountItems < SIZE_OUTPUT_BATCH)
        {
          Containers.Add(container);
          CountItems = container.CountItems;

          break;
        }

        ArchiveImage(ArchiveIndexStore);

        lock (LOCK_OutputQueue)
        {
          ArchiveIndexStore += 1;

          if (OutputQueue.TryGetValue(
            ArchiveIndexStore, out container))
          {
            OutputQueue.Remove(ArchiveIndexStore);
            continue;
          }
          else
          {
            return true;
          }
        }
      }

      IsArchiveLoadCompleted = true;
      return false;
    }



    protected abstract bool TryInsertContainer(
      DataContainer container);



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
          foreach (DataContainer container in 
            Batch.ItemBatchContainers)
          {
            container.Index = ArchiveIndexStore;

            TryInsertContainer(container);
            ArchiveContainer(container);
          }

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



    void ArchiveContainer(DataContainer container)
    {
      Containers.Add(container);
      CountItems += container.CountItems;
      

      if (
        CountItems >= SIZE_OUTPUT_BATCH ||
        container.IsFinalContainer)
      {
        ArchiveContainers(
          ArchiveDirectory.FullName,
          ArchiveIndexStore);

        if (CountItems >= SIZE_OUTPUT_BATCH)
        {
          Containers = new List<DataContainer>();
          CountItems = 0;

          ArchiveImage(ArchiveIndexStore);

          ArchiveIndexStore += 1;
        }
      }
    }


    protected abstract void ArchiveImage(int archiveIndexStore);


    async Task ArchiveContainers(
      string directoryPath,
      int archiveIndex)
    {
      string filePath = Path.Combine(
        directoryPath,
        archiveIndex.ToString());

      while(true)
      {
        try
        {
          using (FileStream file = new FileStream(
            filePath,
            FileMode.Create,
            FileAccess.Write,
            FileShare.None,
            bufferSize: 65536,
            useAsync: true))
          {
            foreach (DataContainer container in Containers)
            {
              await file.WriteAsync(
                container.Buffer,
                0,
                container.Buffer.Length).ConfigureAwait(false);
            }
          }

          return;
        }
        catch (IOException ex)
        {
          Console.WriteLine(ex.GetType().Name + ": " + ex.Message);
          await Task.Delay(2000);
          continue;
        }
        catch (Exception ex)
        {
          Console.WriteLine(ex.GetType().Name + ": " + ex.Message);
          return;
        }
      }
    }


    void ReportInvalidBatch(DataBatch batch)
    {
      Console.WriteLine("Invalid batch {0} reported",
        batch.Index);

      throw new NotImplementedException();
    }
  }
}
