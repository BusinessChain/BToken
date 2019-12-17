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

    protected TaskCompletionSource<object> SignalSynchronizationCompleted =
      new TaskCompletionSource<object>();

    int SizeBatchArchive;
    int CountSyncSessions;

    

    public DataSynchronizer(
      int sizeBatchArchive,
      int countSyncSessions)
    {
      SizeBatchArchive = sizeBatchArchive;
      CountSyncSessions = countSyncSessions;
    }



    public async Task Start()
    {
      Console.WriteLine("Start {0}", GetType().Name);

      LoadImage(out int archiveIndex);
      
      await SynchronizeWithArchive(archiveIndex);
      
      StartBatchSynchronizationBuffer();

      for (int i = 0; i < CountSyncSessions; i += 1)
      {
        RunSyncSession();
      }

      await SignalSynchronizationCompleted.Task;
      SetIsSyncingCompleted();

      Console.WriteLine("{0} completed", GetType().Name);
    }

    protected abstract void LoadImage(
      out int archiveIndexNext);

    protected abstract Task RunSyncSession();


    protected int ArchiveIndexStore;
    const int COUNT_ARCHIVE_LOADER_PARALLEL = 8;
    Task[] ArchiveLoaderTasks = new Task[COUNT_ARCHIVE_LOADER_PARALLEL];

    async Task SynchronizeWithArchive(int archiveIndex)
    {
      ArchiveIndexLoad = archiveIndex + 1;
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

          container.Parse();
        }
        catch
        {
          container.IsValid = false;
        }

      } while (await SendToQueueAndReturnFlagContinue(container));
    }
    
    protected abstract DataContainer CreateContainer(
      int archiveLoadIndex);



    List<DataContainer> Containers = new List<DataContainer>();
    int CountItems;
    Dictionary<int, DataContainer> OutputQueue = 
      new Dictionary<int, DataContainer>();
    bool IsArchiveLoadCompleted;

    async Task<bool> SendToQueueAndReturnFlagContinue(
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

      while (container.IsValid)
      {
        try
        {
          InsertContainer(container);
        }
        catch(ChainException)
        {
          return false;
        }

        if (container.CountItems < SizeBatchArchive)
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


    
    public BufferBlock<DataBatch> BatchSynchronizationBuffer = 
      new BufferBlock<DataBatch>(
        new DataflowBlockOptions { BoundedCapacity = 10 });
    int BatchIndex;

    Dictionary<int, DataBatch> QueueDownloadBatch = 
      new Dictionary<int, DataBatch>();

    async Task StartBatchSynchronizationBuffer()
    {
      while (true)
      {
        DataBatch batch = await BatchSynchronizationBuffer
          .ReceiveAsync()
          .ConfigureAwait(false);

        if (batch.Index != BatchIndex)
        {
          QueueDownloadBatch.Add(batch.Index, batch);
          continue;
        }

        do
        {
          InsertBatch(batch);

          if (batch.IsCancellationBatch)
          {
            SignalSynchronizationCompleted.SetResult(null);
            return;
          }

          BatchIndex += 1;

          if (QueueDownloadBatch.TryGetValue(
            BatchIndex, 
            out batch))
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



    public void InsertBatch(DataBatch batch)
    {
      if (batch.IsCancellationBatch)
      {
        ArchiveContainers();
        return;
      }

      foreach (DataContainer container in
        batch.DataContainers)
      {
        container.Index = ArchiveIndexStore;

        InsertContainer(container);

        Containers.Add(container);
        CountItems += container.CountItems;

        if (CountItems >= SizeBatchArchive)
        {
          ArchiveContainers();

          Containers = new List<DataContainer>();
          CountItems = 0;

          ArchiveImage(ArchiveIndexStore);

          ArchiveIndexStore += 1;
        }
      }
    }



    protected abstract void InsertContainer(
      DataContainer container);



    public async Task ArchiveContainers()
    {
      string filePath = Path.Combine(
        ArchiveDirectory.FullName,
        ArchiveIndexStore.ToString());

      while (true)
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


    protected abstract void ArchiveImage(int archiveIndexStore);


    protected readonly object LOCK_IsSyncingCompleted = new object();
    protected bool IsSyncingCompleted;

    public bool GetIsSyncingCompleted()
    {
      lock (LOCK_IsSyncingCompleted)
      {
        return IsSyncingCompleted;
      }
    }
    protected void SetIsSyncingCompleted()
    {
      lock (LOCK_IsSyncingCompleted)
      {
        IsSyncingCompleted = true;
      }
    }
  }
}
