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
    public partial class HeaderchainSynchronizer : DataSynchronizer
    {
      Headerchain Headerchain;

      bool IsSyncing;

      BufferBlock<Header> HeadersListened =
        new BufferBlock<Header>();

      const int COUNT_HEADER_SESSIONS = 4;
      const int SIZE_BATCH_ARCHIVE = 50000;



      public HeaderchainSynchronizer(Headerchain headerchain)
        : base(SIZE_BATCH_ARCHIVE)
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

        return new DataBatch()
        {
          Index = batchIndex,

          DataContainers = new List<DataContainer>()
          { 
            new HeaderContainer(locatorHashes)
          }
        };
      }



      public void ReportInvalidBatch(DataBatch batch)
      {
        Console.WriteLine("Invalid batch {0} reported",
          batch.Index);

        throw new NotImplementedException();
      }


      protected override void LoadImage(out int archiveIndexNext)
      {
        archiveIndexNext = 0;
      }

      protected override void ArchiveImage(int archiveIndex)
      { }

      protected override DataContainer CreateContainer(
        int index)
      {
        return new HeaderContainer(index);
      }

           

      public bool TryInsertHeaderBytes(
        byte[] buffer)
      {
        DataBatch batch = new DataBatch()
        {
          DataContainers = new List<DataContainer>()
        {
          new HeaderContainer(buffer)
        },

          IsFinalBatch = true
        };

        batch.TryParse();

        if(TryInsertBatch(batch))
        {
          foreach(HeaderContainer headerContainer 
            in batch.DataContainers)
          {
            Header header = headerContainer.HeaderRoot;
            while (true)
            {
              Console.WriteLine("inserted header {0}, height {1}",
                header.HeaderHash.ToHexString(),
                Headerchain.GetHeight());

              if (header == headerContainer.HeaderTip)
              {
                break;
              }

              header = header.HeadersNext.First();
            }
          }

          return true;
        }

        return false;
      }



      protected override bool TryInsertContainer(
        DataContainer container)
      {
        try
        {
          Headerchain.InsertContainer(
            (HeaderContainer)container);

          return true;
        }
        catch(ChainException ex)
        {
          Console.WriteLine(
            "Insertion of headerContainer {0} raised ChainException:\n {1}.",
            container.Index,
            ex.Message);

          return false;
        }
      }


      
    }
  }
}
