using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.IO;
using System.Threading.Tasks;
using System.Threading.Tasks.Dataflow;

namespace BToken.Chaining
{
  partial class Headerchain
  {
    partial class HeaderchainSynchronizer : DataSynchronizer
    {
      Headerchain Headerchain;

      readonly object LOCK_IsSyncing = new object();
      bool IsSyncing;
      bool IsSyncingCompleted;

      BufferBlock<Header> HeadersListened =
        new BufferBlock<Header>();

      const int COUNT_HEADER_SESSIONS = 4;

      

      public HeaderchainSynchronizer(Headerchain headerchain)
      {
        ArchiveDirectory = Directory.CreateDirectory(
          Path.Combine(
            AppDomain.CurrentDomain.BaseDirectory,
            "HeaderArchive"));

        Headerchain = headerchain;
      }


      protected override Task[] StartSyncSessionTasks()
      {
        Task[] syncTasks = new Task[COUNT_HEADER_SESSIONS];

        for (int i = 0; i < COUNT_HEADER_SESSIONS; i += 1)
        {
          syncTasks[i] = new SyncHeaderchainSession(this).Start();
        }

        return syncTasks;
      }



      IEnumerable<byte[]> LocatorHashes;
      int IndexHeaderBatch;
      DataBatch HeaderBatchOld;
      TaskCompletionSource<object> SignalStartHeaderSyncSession =
        new TaskCompletionSource<object>();

      DataBatch CreateHeaderBatch()
      {
        int batchIndex;
        IEnumerable<byte[]> locatorHashes;

        lock (LOCK_IsSyncing)
        {
          batchIndex = IndexHeaderBatch;

          if (LocatorHashes == null)
          {
            lock (Headerchain.LOCK_Chain)
            {
              LocatorHashes = Headerchain.Locator.GetHeaderHashes();
            }
          }

          locatorHashes = LocatorHashes;
        }

        var headerBatch = new DataBatch(batchIndex);

        headerBatch.ItemBatchContainers.Add(
          new HeaderBatchContainer(
            headerBatch,
            locatorHashes));

        return headerBatch;
      }



      public void ReportInvalidBatch(DataBatch batch)
      {
        Console.WriteLine("Invalid batch {0} reported",
          batch.Index);

        throw new NotImplementedException();
      }


      protected override void LoadImage(out int archiveIndexNext)
      {
        Headerchain.LoadImage(out archiveIndexNext);
      }

      protected override DataContainer CreateContainer(int archiveLoadIndex)
      {
        return new HeaderBatchContainer(archiveLoadIndex);
      }


      protected override void InsertContainer(
        DataContainer container)
      {
        Headerchain.InsertContainer(
          (HeaderBatchContainer)container);
      }


      protected override bool TryInsertContainer(
        DataContainer container)
      {
        try
        {
          InsertContainer(container);

          return true;
        }
        catch(ChainException)
        {
          return false;
        }
      }
    }
  }
}
