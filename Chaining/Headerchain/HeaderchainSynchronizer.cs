using System;
using System.Collections.Generic;
using System.Linq;
using System.Collections.Concurrent;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Threading.Tasks.Dataflow;

using BToken.Networking;


namespace BToken.Chaining
{
  partial class Headerchain
  {
    public partial class HeaderchainSynchronizer : DataSynchronizer
    {
      Headerchain Headerchain;

      readonly object LOCK_IsAnySessionSyncing = new object();
      bool IsAnySessionSyncing;

      BufferBlock<Header> HeadersListened =
        new BufferBlock<Header>();

      const int COUNT_HEADER_SESSIONS = 4;
      const int SIZE_BATCH_ARCHIVE = 50000;

      const int TIMEOUT_GETHEADERS_MILLISECONDS = 5000;
      DataBatch HeaderBatch;
      
      ConcurrentQueue<DataBatch> QueueBatchesCanceled
        = new ConcurrentQueue<DataBatch>();


      public HeaderchainSynchronizer(Headerchain headerchain)
        : base(
            SIZE_BATCH_ARCHIVE,
            COUNT_HEADER_SESSIONS)
      {
        ArchiveDirectory = Directory.CreateDirectory(
          Path.Combine(
            AppDomain.CurrentDomain.BaseDirectory,
            "HeaderArchive"));

        Headerchain = headerchain;
      }



      protected override async Task RunSyncSession()
      {
        while(true)
        {
          Network.INetworkChannel channel =
            await Headerchain.Network.RequestChannel();

          lock (LOCK_IsAnySessionSyncing)
          {
            if (IsAnySessionSyncing)
            {
              channel.Release();
              return;
            }

            IsAnySessionSyncing = true;
          }

          try
          {
            do
            {
              LoadBatch();

              await DownloadHeaders(channel);

              if(HeaderBatch.CountItems == 0)
              {
                HeaderBatch.IsCancellationBatch = true;
              }

              await BatchSynchronizationBuffer
                .SendAsync(HeaderBatch);

            } while (!HeaderBatch.IsCancellationBatch);
            
            channel.Release();

            return;
          }
          catch (Exception ex)
          {
            Console.WriteLine(
              "{0} in SyncHeaderchainSession {1} with channel {2}: '{3}'",
              ex.GetType().Name,
              GetHashCode(),
              channel == null ? "'null'" : channel.GetIdentification(),
              ex.Message);

            QueueBatchesCanceled.Enqueue(HeaderBatch);

            channel.Dispose();

            lock (LOCK_IsAnySessionSyncing)
            {
              IsAnySessionSyncing = false;
            }
          }
        }
      }



      public void LoadBatch()
      {
        if (QueueBatchesCanceled.TryDequeue(out DataBatch headerBatch))
        {
          HeaderBatch = headerBatch;
          return;
        }

        if (HeaderBatch == null || HeaderBatch.IsCancellationBatch)
        {
          IEnumerable<byte[]> headerLocator;

          lock (Headerchain.LOCK_Chain)
          {
            headerLocator = Headerchain.Locator.GetHeaderHashes();
          }
          
          HeaderBatch = new DataBatch()
          {
            Index = 0,

            DataContainers = new List<DataContainer>()
            {
              new HeaderContainer(headerLocator)
            }
          };

          return;
        }

        HeaderBatch = new DataBatch()
        {
          Index = HeaderBatch.Index + 1,

          DataContainers = new List<DataContainer>()
          {
            new HeaderContainer(
              HeaderBatch.DataContainers
              .Select(d => ((HeaderContainer)d).HeaderTip.HeaderHash))
          }
        };
      }
      


      public async Task DownloadHeaders(Network.INetworkChannel channel)
      {
        int timeout = TIMEOUT_GETHEADERS_MILLISECONDS;

        CancellationTokenSource cancellation = new CancellationTokenSource(timeout);

        HeaderBatch.CountItems = 0;

        foreach (HeaderContainer headerBatchContainer
          in HeaderBatch.DataContainers)
        {
          headerBatchContainer.Buffer = await channel.GetHeaders(
            headerBatchContainer.LocatorHashes,
            cancellation.Token);

          headerBatchContainer.TryParse();

          HeaderBatch.CountItems += headerBatchContainer.CountItems;
        }
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


      public bool TryInsertHeaderBytes(byte[] buffer)
      {
        HeaderBatch = new DataBatch()
        {
          DataContainers = new List<DataContainer>()
          {
            new HeaderContainer(buffer)
          }
        };

        HeaderBatch.TryParse();

        return TryInsertBatch();
      }

      public bool TryInsertBatch()
      {
        if (TryInsertBatch(HeaderBatch))
        {
          ArchiveContainers();
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
        catch(ChainException)
        {
          return false;
        }
        catch(Exception ex)
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
