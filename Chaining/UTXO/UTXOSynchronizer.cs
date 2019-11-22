﻿using System;
using System.Diagnostics;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Linq;
using System.IO;
using System.Threading.Tasks;
using System.Threading.Tasks.Dataflow;

using BToken.Networking;



namespace BToken.Chaining
{
  partial class UTXOTable
  {
    public partial class UTXOSynchronizer : DataSynchronizer
    {
      UTXOTable UTXOTable;

      public Dictionary<byte[], int> MapBlockToArchiveIndex = 
        new Dictionary<byte[], int>(new EqualityComparerByteArray());

      const int COUNT_UTXO_SESSIONS = 4;
      const int SIZE_BATCH_ARCHIVE = 50000;

      const int TIMEOUT_BLOCKDOWNLOAD_MILLISECONDS = 20000;

      const int UTXOSTATE_ARCHIVING_INTERVAL = 50;
      const int COUNT_BLOCKS_DOWNLOADBATCH_INIT = 1;



      public UTXOSynchronizer(UTXOTable uTXOTable)
        : base(
            SIZE_BATCH_ARCHIVE,
            COUNT_UTXO_SESSIONS)
      {
        UTXOTable = uTXOTable;

        ArchiveDirectory = Directory.CreateDirectory(
          "J:\\BlockArchivePartitioned");
      }


      protected override async Task RunSyncSession()
      {
        Stopwatch stopwatchDownload = new Stopwatch();
        int countBlocks = COUNT_BLOCKS_DOWNLOADBATCH_INIT;
        DataBatch uTXOBatch = null;
        
        while(true)
        {
          UTXOChannel channel = new UTXOChannel(
            await UTXOTable.Network.RequestChannel());

          Console.WriteLine(
            "{0} requested channel {1}",
            GetType().Name,
            channel.NetworkChannel.GetIdentification());

          try
          {
            do
            {
              uTXOBatch = LoadBatch(countBlocks);

              stopwatchDownload.Restart();

              await channel.DownloadBlocks(uTXOBatch);
              
              stopwatchDownload.Stop();

              await BatchSynchronizationBuffer.SendAsync(uTXOBatch);

              CalculateNewCountBlocks(
                ref countBlocks,
                stopwatchDownload.ElapsedMilliseconds);

            } while (!uTXOBatch.IsCancellationBatch);

            channel.Release();

            return;
          }
          catch (Exception ex)
          {
            Console.WriteLine("Exception {0} in block download: \n{1}" +
              "batch {2} queued",
              ex.GetType().Name,
              ex.Message,
              uTXOBatch.Index);

            QueueBatchesCanceled.Enqueue(uTXOBatch);

            channel.Dispose();

            countBlocks = COUNT_BLOCKS_DOWNLOADBATCH_INIT;
          }
        }
      }

      static void CalculateNewCountBlocks(ref int countBlocks, long timeDownloadMilliseconds)
      {
        const float safetyFactorTimeout = 10;
        const float marginFactorResetCountBlocksDownload = 5;

        float ratioTimeoutToDownloadTime = TIMEOUT_BLOCKDOWNLOAD_MILLISECONDS
          / (1 + timeDownloadMilliseconds);

        if (ratioTimeoutToDownloadTime > safetyFactorTimeout)
        {
          countBlocks += 1;
        }
        else if (ratioTimeoutToDownloadTime < marginFactorResetCountBlocksDownload)
        {
          countBlocks = COUNT_BLOCKS_DOWNLOADBATCH_INIT;
        }
        else if (countBlocks > 1)
        {
          countBlocks -= 1;
        }
      }



      ConcurrentQueue<DataBatch> QueueBatchesCanceled
        = new ConcurrentQueue<DataBatch>();
      static readonly object LOCK_LoadBatch = new object();
      int IndexLoad;
      Header HeaderLoad;

      DataBatch LoadBatch(int countHeaders)
      {
        if (QueueBatchesCanceled.TryDequeue(out DataBatch uTXOBatch))
        {
          return uTXOBatch;
        }

        lock(LOCK_LoadBatch)
        {
          uTXOBatch = new DataBatch(IndexLoad++);

          if (HeaderLoad == null)
          {
            HeaderLoad = UTXOTable.Header;
          }

          for (int i = 0; i < countHeaders; i += 1)
          {
            if (HeaderLoad.HeadersNext.Count == 0)
            {
              uTXOBatch.IsCancellationBatch = (i == 0);
              return uTXOBatch;
            }

            HeaderLoad = HeaderLoad.HeadersNext[0];

            BlockContainer blockContainer =
              new BlockContainer(
                UTXOTable.Headerchain,
                HeaderLoad);

            uTXOBatch.DataContainers.Add(blockContainer);
          }
        }

        return uTXOBatch;
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
          Console.WriteLine(
            "HeaderPrevious {0} of batch {1} not equal to \nHeaderMergedLast {2}",
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

        blockContainer.Headers
          .ForEach(h => MapBlockToArchiveIndex.Add(
            h.HeaderHash, 
            blockContainer.Index));

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
           FileAccess.Write))
        {
          stream.Write(uTXOState, 0, uTXOState.Length);
        }

        using (FileStream stream = new FileStream(
           Path.Combine(PathUTXOState, "MapBlockHeader"),
           FileMode.Create,
           FileAccess.Write))
        {
          foreach(KeyValuePair<byte[], int> keyValuePair 
            in MapBlockToArchiveIndex)
          {
            stream.Write(keyValuePair.Key, 0, keyValuePair.Key.Length);

            byte[] valueBytes = BitConverter.GetBytes(keyValuePair.Value);
            stream.Write(valueBytes, 0, valueBytes.Length);
          }
        }

        UTXOTable.BackupToDisk();
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

        while (true)
        {
          DataBatch batch = LoadBatch(1);

          try
          {
            await uTXOChannel.DownloadBlocks(batch);
          }
          catch
          {
            Console.WriteLine(
              "could not download batch {0} from {1}",
              batch.Index,
              channel.GetIdentification());

            UTXOTable.UnLoadBatch(batch);
            return false;
          }
          
          if (TryInsertBatch(batch))
          {
            if(batch.IsCancellationBatch)
            {
              return true;
            }

            Console.WriteLine(
              "inserted batch {0} in UTXO table",
              batch.Index);
          }
          else
          {
            Console.WriteLine(
              "could not insert batch {0} in UTXO table",
              batch.Index);

            return false;
          }
        }

      }

    }
  }
}
