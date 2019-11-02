using System;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Linq;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

using BToken.Networking;



namespace BToken.Chaining
{
  partial class UTXOTable
  {
    partial class UTXOSynchronizer : DataSynchronizer
    {
      UTXOTable UTXOTable;

      const int COUNT_UTXO_SESSIONS = 4;
      const int SIZE_BATCH_ARCHIVE = 50000;



      public UTXOSynchronizer(UTXOTable uTXOTable)
        : base(SIZE_BATCH_ARCHIVE)
      {
        UTXOTable = uTXOTable;

        ArchiveDirectory = Directory.CreateDirectory(
          "J:\\BlockArchivePartitioned");
      }

      

      protected override Task[] StartSyncSessionTasks()
      {
        UTXOTable.HeaderLoad = UTXOTable.Header;

        Task[] syncTasks = new Task[COUNT_UTXO_SESSIONS];

        for (int i = 0; i < COUNT_UTXO_SESSIONS; i += 1)
        {
          syncTasks[i] = new SyncUTXOSession(this).Start();
        }

        return syncTasks;
      }



      ConcurrentQueue<DataBatch> QueueBatchesCanceled
        = new ConcurrentQueue<DataBatch>();

      bool TryGetBatch(
        out DataBatch uTXOBatch,
        int countHeaders)
      {
        if (QueueBatchesCanceled.TryDequeue(out uTXOBatch))
        {
          return true;
        }

        return UTXOTable.TryLoadBatch(
           out uTXOBatch,
           countHeaders);
      }


      protected override void LoadImage(out int archiveIndex)
      {
        UTXOTable.LoadImage(out archiveIndex);
      }

      

      protected override bool TryInsertContainer(
        DataContainer container)
      {
        BlockContainer blockContainer = (BlockContainer)container;

        if (blockContainer.HeaderPrevious != UTXOTable.Header)
        {
          Console.WriteLine("HeaderPrevious {0} of batch {1} not equal to \nHeaderMergedLast {2}",
            blockContainer.HeaderPrevious.HeaderHash.ToHexString(),
            blockContainer.Index,
            UTXOTable.Header.HeaderHash.ToHexString());

          return false;
        }

        try
        {
          UTXOTable.InsertContainer(blockContainer);
        }
        catch (ChainException ex)
        {
          Console.WriteLine(
            "Insertion of blockBatchContainer {0} raised ChainException:\n {1}.",
            container.Index,
            ex.Message);

          return false;
        }

        UTXOTable.LogInsertion(blockContainer);

        return true;
      }

      protected override void ArchiveImage(int archiveIndex)
      {
        if (archiveIndex % UTXOSTATE_ARCHIVING_INTERVAL != 0)
        {
          return;
        }

        if (Directory.Exists(PathUTXOState))
        {
          if (Directory.Exists(PathUTXOStateOld))
          {
            Directory.Delete(PathUTXOStateOld, true);
          }
          Directory.Move(PathUTXOState, PathUTXOStateOld);
        }

        Directory.CreateDirectory(PathUTXOState);

        byte[] uTXOState = new byte[40];
        BitConverter.GetBytes(archiveIndex).CopyTo(uTXOState, 0);
        BitConverter.GetBytes(UTXOTable.BlockHeight).CopyTo(uTXOState, 4);
        UTXOTable.Header.HeaderHash.CopyTo(uTXOState, 8);

        using (FileStream stream = new FileStream(
           Path.Combine(PathUTXOState, "UTXOState"),
           FileMode.Create,
           FileAccess.ReadWrite,
           FileShare.Read))
        {
          stream.Write(uTXOState, 0, uTXOState.Length);
        }

        Parallel.ForEach(UTXOTable.Tables, t =>
        {
          t.BackupToDisk(PathUTXOState);
        });
      }

      async Task<UTXOChannel> RequestChannel()
      {
        return new UTXOChannel(
          await UTXOTable.Network.RequestChannel());
      }

      protected override DataContainer CreateContainer(
        int index)
      {
        return new BlockContainer(
          UTXOTable.Headerchain,
          index);
      }



      public async Task<bool> TrySynchronize(
        Network.INetworkChannel channel)
      {
        UTXOChannel uTXOChannel = new UTXOChannel(channel);

        while (UTXOTable.TryLoadBatch(
          out DataBatch batch, 1))
        {
          try
          {
            await uTXOChannel.StartBlockDownloadAsync(
              batch);
          }
          catch
          {
            Console.WriteLine("could not download batch {0} from {1}",
              batch.Index,
              channel.GetIdentification());

            UTXOTable.UnLoadBatch(batch);
            return false;
          }


          if (TryInsertBatch(batch))
          {
            Console.WriteLine("inserted batch {0} in UTXO table",
              batch.Index);
          }
          else
          {
            Console.WriteLine("could not insert batch {0} in UTXO table",
              batch.Index);

            return false;
          }
        }

        return true;
      }



      const int TIMEOUT_BLOCKDOWNLOAD_MILLISECONDS = 20000;

      public async Task StartBlockDownloadAsync(
        DataBatch batch,
        Network.INetworkChannel channel)
      {
        List<byte[]> hashesRequested = new List<byte[]>();

        foreach (BlockContainer blockBatchContainer in
          batch.DataContainers)
        {
          if (blockBatchContainer.Buffer == null)
          {
            hashesRequested.Add(
              blockBatchContainer.Header.HeaderHash);
          }
        }
        
        await channel.SendMessage(
          new GetDataMessage(
            hashesRequested
            .Select(h => new Inventory(
              InventoryType.MSG_BLOCK,
              h))));

        var cancellationDownloadBlocks =
          new CancellationTokenSource(TIMEOUT_BLOCKDOWNLOAD_MILLISECONDS);

        foreach (BlockContainer blockBatchContainer in
          batch.DataContainers)
        {
          if (blockBatchContainer.Buffer != null)
          {
            continue;
          }
          
          while (true)
          {
            NetworkMessage networkMessage =
              await channel
              .ReceiveApplicationMessage(cancellationDownloadBlocks.Token)
              .ConfigureAwait(false);

            if (networkMessage.Command != "block")
            {
              continue;
            }

            break;
          }

          blockBatchContainer.TryParse();
          batch.CountItems += blockBatchContainer.CountItems;
        }
      }
    }
  }
}
