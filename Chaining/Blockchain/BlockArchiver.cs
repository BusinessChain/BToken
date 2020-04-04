using System;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using System.Threading.Tasks;
using System.Security.Cryptography;

namespace BToken.Chaining
{
  public partial class Blockchain
  {
    public class DataArchiver
    {
      public interface IDataStructure
      {
        void InsertContainer(DataContainer container);
        DataContainer CreateContainer(int archiveLoadIndex);
        void LoadImage();

        void ArchiveImage(int archiveIndexStore);
      }



      IDataStructure DataStructure;

      readonly DirectoryInfo ArchiveDirectory =
        Directory.CreateDirectory("J:\\BlockArchivePartitioned");

      const int SIZE_BATCH_ARCHIVE = 50000;
      int CountSyncSessions;

      public int ArchiveIndex;
      public List<UTXOTable.BlockContainer> Containers =
        new List<UTXOTable.BlockContainer>();
      public int CountTX;

      SHA256 SHA256 = SHA256.Create();


      public DataArchiver(
        IDataStructure dataStructure,
        int countSyncSessions)
      {
        DataStructure = dataStructure;
        CountSyncSessions = countSyncSessions;
      }



      int ArchiveIndexLoad;
      const int COUNT_ARCHIVE_LOADER_PARALLEL = 8;
      Task[] ArchiveLoaderTasks = new Task[COUNT_ARCHIVE_LOADER_PARALLEL];

      public async Task Load(byte[] hashInsertedLast)
      {
        DataStructure.LoadImage();

        ArchiveIndexLoad = archiveIndex + 1;
        ArchiveIndex = ArchiveIndexLoad;

        Parallel.For(
          0,
          COUNT_ARCHIVE_LOADER_PARALLEL,
          i => ArchiveLoaderTasks[i] = StartArchiveLoaderAsync());

        await Task.WhenAll(ArchiveLoaderTasks);
      }



      readonly object LOCK_ArchiveLoadIndex = new object();

      async Task StartArchiveLoaderAsync()
      {
        SHA256 sHA256 = SHA256.Create();
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

            container.Parse(sHA256);
          }
          catch
          {
            container.IsValid = false;
          }

        } while (await SendToQueueAndReturnFlagContinue(container));
      }



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
            if (container.Index == ArchiveIndex)
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

          if (container.CountTX < SIZE_BATCH_ARCHIVE)
          {
            Containers.Add(container);
            CountTX = container.CountTX;

            break;
          }

          DataStructure.ArchiveImage(ArchiveIndex);

          lock (LOCK_OutputQueue)
          {
            ArchiveIndex += 1;

            if (OutputQueue.TryGetValue(
              ArchiveIndex, out container))
            {
              OutputQueue.Remove(ArchiveIndex);
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


      public async Task ArchiveContainer(
        UTXOTable.BlockContainer container)
      {
        Containers.Add(container);
        CountTX += container.CountTX;

        if (CountTX >= SIZE_BATCH_ARCHIVE)
        {
          string filePath = Path.Combine(
            "branch",
            ArchiveIndex.ToString());

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
                foreach (UTXOTable.BlockContainer c
                  in Containers)
                {
                  await file.WriteAsync(
                    c.Buffer,
                    0,
                    c.Buffer.Length);
                }
              }

              break;
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
              break;
            }
          }

          Containers.Clear();
          CountTX = 0;

          if (ArchiveIndex % UTXOSTATE_ARCHIVING_INTERVAL == 0)
          {
            ArchiveImage();
          }

          ArchiveIndex += 1;
        }
      }

      public UTXOTable.BlockContainer PopContainer(Header header)
      {
        UTXOTable.BlockContainer blockContainer;

        if (Containers.Count == 0)
        {
          ArchiveIndex -= 1;

          blockContainer = new UTXOTable.BlockContainer(
            File.ReadAllBytes(
              Path.Combine("main", ArchiveIndex.ToString())));

          blockContainer.Parse(SHA256);

          Containers = blockContainer.Split();
        }

        blockContainer = Containers.Last();

        blockContainer.ValidateHeaderHash(header.Hash);

        CountTX -= blockContainer.CountTX;
        Containers.RemoveAt(Containers.Count);

        return blockContainer;
      }

      public void Branch(DataArchiver archiver)
      {
        ArchiveDirectory.GetFiles()
          .ToList()
          .ForEach(f => f.Delete());

        Containers = archiver.Containers;
        ArchiveIndex = archiver.ArchiveIndex;
        CountTX = archiver.CountTX;
      }

      public void Export(DataArchiver archiverDestination)
      {
        FileInfo[] files = ArchiveDirectory.GetFiles();

        int i = 0;
        while (i < files.Length)
        {
          string nameFileDest = Path.Combine(
                archiverDestination.ArchiveDirectory.Name,
                files[i].Name);
          try
          {
            files[i].MoveTo(nameFileDest);
          }
          catch (IOException)
          {
            File.Delete(nameFileDest);
            continue;
          }

          i += 1;
        }

        archiverDestination.Containers = Containers;
        archiverDestination.CountTX = CountTX;
        archiverDestination.ArchiveIndex = ArchiveIndex;
      }
    }
  }


}
