using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Threading.Tasks.Dataflow;

using BToken.Networking;

namespace BToken.Chaining
{
  partial class Headerchain
  {
    partial class HeaderchainSynchronizer : DataSynchronizer
    {
      Headerchain Headerchain;
      Network Network;

      readonly object LOCK_IsSyncing = new object();
      bool IsSyncing;
      bool IsSyncingCompleted;

      BufferBlock<Header> HeadersListened =
        new BufferBlock<Header>();

      const int COUNT_HEADER_SESSIONS = 4;



      public HeaderchainSynchronizer(
        Network network,
        Headerchain headerchain)
        : base(COUNT_HEADER_SESSIONS)
      {
        Network = network;
        Headerchain = headerchain;
      }

      

      protected override Task CreateSyncSessionTask()
      {
        return new SyncHeaderchainSession(this).Start();
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

      protected override DataBatchContainer LoadDataContainer(
        int containerIndex)
      {
        return Headerchain.LoadDataContainer(containerIndex);
      }


      protected override bool TryInsertContainer(
        DataBatchContainer container)
      {
        if(Headerchain.TryInsertContainer(
          (HeaderBatchContainer)container))
        {
          Headerchain.ArchiveIndex += 1;
          return true;
        }

        return false;

      }

      protected override bool TryInsertBatch(DataBatch batch)
      {
        return Headerchain.TryInsertBatch(batch);
      }

    }
  }
}
