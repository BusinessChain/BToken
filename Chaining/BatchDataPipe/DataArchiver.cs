using System;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using System.Threading.Tasks;

namespace BToken.Chaining
{
  public class DataArchiver
  {
    public interface IDataStructure
    {
      void InsertContainer(DataContainer container);
      DataContainer CreateContainer(int archiveLoadIndex);

      void LoadImage(out int archiveIndexNext);
      void ArchiveImage(int archiveIndexStore);
    }



    IDataStructure DataStructure;


    DirectoryInfo ArchiveDirectory;
    int SizeBatchArchive;
    int CountSyncSessions;



    public DataArchiver(
      IDataStructure dataStructure,
      string pathArchive,
      int sizeBatchArchive,
      int countSyncSessions)
    {
      DataStructure = dataStructure;
      ArchiveDirectory = Directory.CreateDirectory(pathArchive);
      SizeBatchArchive = sizeBatchArchive;
      CountSyncSessions = countSyncSessions;
    }



    int ArchiveIndexLoad;
    int ArchiveIndexStore;
    const int COUNT_ARCHIVE_LOADER_PARALLEL = 8;
    Task[] ArchiveLoaderTasks = new Task[COUNT_ARCHIVE_LOADER_PARALLEL];

    public async Task Load()
    {
      DataStructure.LoadImage(out int archiveIndex);

      ArchiveIndexLoad = archiveIndex + 1;
      ArchiveIndexStore = ArchiveIndexLoad;

      Parallel.For(
        0,
        COUNT_ARCHIVE_LOADER_PARALLEL,
        i => ArchiveLoaderTasks[i] = StartArchiveLoaderAsync());

      await Task.WhenAll(ArchiveLoaderTasks);
    }

         

    readonly object LOCK_ArchiveLoadIndex = new object();

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

        container = DataStructure.CreateContainer(
          archiveLoadIndex);

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
    


    List<DataContainer> Containers = new List<DataContainer>();
    int CountItems;
    Dictionary<int, DataContainer> OutputQueue =
      new Dictionary<int, DataContainer>();
    bool IsArchiveLoadCompleted;
    readonly object LOCK_OutputQueue = new object();

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
          DataStructure.InsertContainer(container);
        }
        catch (ChainException)
        {
          return false;
        }

        if (container.CountItems < SizeBatchArchive)
        {
          Containers.Add(container);
          CountItems = container.CountItems;

          break;
        }

        DataStructure.ArchiveImage(ArchiveIndexStore);

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
  }
}
