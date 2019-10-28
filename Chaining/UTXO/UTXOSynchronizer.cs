using System;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Linq;
using System.IO;
using System.Threading.Tasks;

using BToken.Networking;



namespace BToken.Chaining
{
  partial class UTXOTable
  {
    partial class UTXOSynchronizer : DataSynchronizer
    {
      UTXOTable UTXOTable;
      string ArchivePath = "J:\\BlockArchivePartitioned";

      const int COUNT_UTXO_SESSIONS = 4;



      public UTXOSynchronizer(UTXOTable uTXOTable)
      {
        UTXOTable = uTXOTable;

        ArchiveDirectory = Directory.CreateDirectory(ArchivePath);
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
        BlockBatchContainer blockContainer = 
          (BlockBatchContainer)container;

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
        
        UTXOTable.LogInsertion(
          blockContainer.StopwatchParse.ElapsedTicks,
          container.Index);

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
        return new BlockBatchContainer(
          UTXOTable.Headerchain,
          index);
      }

      void ReturnChannel(UTXOChannel channel)
      {
        UTXOTable.Network.ReturnChannel(
          channel.NetworkChannel);
      }

      void DisposeChannel(UTXOChannel channel)
      {
        UTXOTable.Network.DisposeChannel(channel.NetworkChannel);
      }
    }
  }
}
