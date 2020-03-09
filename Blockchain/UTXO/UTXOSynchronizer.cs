using System;
using System.Diagnostics;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Linq;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Threading.Tasks.Dataflow;




namespace BToken.Blockchain
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

      const int UTXOSTATE_ARCHIVING_INTERVAL = 10;
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
            await UTXOTable.Network.DispatchChannelOutbound());

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

      static void CalculateNewCountBlocks(
        ref int countBlocks, 
        long timeDownloadMilliseconds)
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
            if (HeaderLoad.HeaderNext == null)
            {
              uTXOBatch.IsCancellationBatch = (i == 0);
              return uTXOBatch;
            }

            HeaderLoad = HeaderLoad.HeaderNext;

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

      

      protected override void InsertContainer(
        DataContainer container)
      {
        BlockContainer blockContainer = (BlockContainer)container;

        if (blockContainer.HeaderPrevious != UTXOTable.Header)
        {
          throw new ChainException(
            string.Format(
              "HeaderPrevious {0} of batch {1} not equal to \nHeaderMergedLast {2}.",
              blockContainer.HeaderPrevious.Hash.ToHexString(),
              blockContainer.Index,
              UTXOTable.Header.Hash.ToHexString()),
            ErrorCode.ORPHAN);
        }

        UTXOTable.StageContainer(blockContainer);

        blockContainer.Headers
          .ForEach(h => MapBlockToArchiveIndex.Add(
            h.Hash, 
            blockContainer.Index));
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
        UTXOTable.Header.Hash.CopyTo(uTXOState, 8);

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


      
      Dictionary<int, BlockContainer> CacheBlockContainers =
        new Dictionary<int, BlockContainer>();
      List<int> ArchiveIndexesInCache = new List<int>();
      List<int> ArchiveIndexesLoading = new List<int>();
      readonly object LOCK_CacheBlockContainers = new object();

      public List<byte[]> GetBlocks(
        IEnumerable<byte[]> hashes)
      {
        List<byte[]> blocks = new List<byte[]>();

        foreach (byte[] hash in hashes)
        {
          if (!MapBlockToArchiveIndex.TryGetValue(
            hash,
            out int archiveIndex))
          {
            blocks.Add(null);
            continue;
          }

          BlockContainer container;
          bool sleepThread = false;

          while (true)
          {
            if (sleepThread)
            {
              Thread.Sleep(10);
            }

            lock (LOCK_CacheBlockContainers)
            {
              if (CacheBlockContainers.TryGetValue(
                archiveIndex,
                out container))
              {
                break;
              }

              if (ArchiveIndexesLoading.Contains(archiveIndex))
              {
                sleepThread = true;
                continue;
              }

              ArchiveIndexesLoading.Add(archiveIndex);
              break;
            }
          }

          if (container == null)
          {
            container = new BlockContainer(
               UTXOTable.Headerchain,
               archiveIndex);

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
              blocks.Add(null);
              continue;
            }

            lock (LOCK_CacheBlockContainers)
            {
              if (ArchiveIndexesInCache.Count > 10)
              {
                int archiveIndexRemove = ArchiveIndexesInCache[0];
                ArchiveIndexesInCache.RemoveAt(0);
                CacheBlockContainers.Remove(archiveIndexRemove);
              }

              CacheBlockContainers.Add(
                archiveIndex,
                container);

              ArchiveIndexesInCache.Add(archiveIndex);
            }
          }

          container
            .BufferStartIndexAndLengthBlocks
            .TryGetValue(hash, out int[] startIndexAndLength);

          byte[] blockBytes = new byte[startIndexAndLength[1]];

          Array.Copy(
            container.Buffer,
            startIndexAndLength[0],
            blockBytes,
            0,
            startIndexAndLength[1]);

          blocks.Add(blockBytes);
        }

        return blocks;
      }
    }
  }
}
