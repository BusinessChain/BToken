using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Threading.Tasks.Dataflow;
using System.Security.Cryptography;


using BToken.Chaining;

namespace BToken.Accounting
{
  public partial class UTXO
  {
    partial class UTXOBuilder
    {
      UTXO UTXO;

      const int COUNT_ARCHIVE_BATCHES_PARALLEL = 8;
      const int COUNT_DOWNLOAD_TASKS = 8;
      const int COUNT_BLOCKS_DOWNLOAD = 10;
      const int COUNT_TXS_IN_BATCH_FILE = 100;

      readonly object LOCK_BatchFileIndex = new object();
      int BatchFileIndex;
      readonly object LOCK_BatchIndexMerge = new object();
      int BatchIndexMerge;
      readonly object LOCK_DownloadIndex = new object();
      int DownloadIndex;
      int DownloadBatcherIndex;
      public SHA256 SHA256 = SHA256.Create();

      BufferBlock<UTXODownloadBatch> DownloadBuffer = new BufferBlock<UTXODownloadBatch>();
      Dictionary<int, UTXODownloadBatch> QueueDownloadBatch = new Dictionary<int, UTXODownloadBatch>();

      Queue<Block> FIFOBlocks = new Queue<Block>();
      int TXCountFIFO;

      BufferBlock<UTXOBatch> BatchParserBuffer = new BufferBlock<UTXOBatch>();


      public UTXOBuilder(UTXO uTXO)
      {
        UTXO = uTXO;
      }

      public async Task RunAsync()
      {
        BatchFileIndex = UTXO.BatchIndexNextMerger;
        BatchIndexMerge = UTXO.BatchIndexNextMerger;

        await RunArchiveLoaderAsync();

        await RunNetworkLoaderAsync();
      }

      async Task RunArchiveLoaderAsync()
      {
        Task[] archiveLoaderTasks = new Task[COUNT_ARCHIVE_BATCHES_PARALLEL];
        for (int i = 0; i < COUNT_ARCHIVE_BATCHES_PARALLEL; i += 1)
        {
          archiveLoaderTasks[i] = LoadBatchesFromArchiveAsync();
        }
        await Task.WhenAll(archiveLoaderTasks);
      }
      async Task LoadBatchesFromArchiveAsync()
      {
        int batchIndex;

        try
        {
          while (true)
          {
            lock (LOCK_BatchFileIndex)
            {
              batchIndex = BatchFileIndex;
              BatchFileIndex += 1;
            }

            if (!BlockArchiver.Exists(batchIndex, out string filePath))
            {
              return;
            }

            UTXOBatch batch = new UTXOBatch()
            {
              BatchIndex = batchIndex,
              Buffer = await BlockArchiver.ReadBlockBatchAsync(filePath).ConfigureAwait(false)
            };

            UTXO.Parser.ParseBatch(batch);

            lock (LOCK_BatchIndexMerge)
            {
              UTXO.Merger.BatchBuffer.Post(batch);
              BatchIndexMerge += 1;
              UTXO.HeaderHashBatchedLast = batch.Blocks.Last().HeaderHash;
            }
          }
        }
        catch (Exception ex)
        {
          Console.WriteLine(ex.Message);
          throw ex;
        }
      }

      async Task RunNetworkLoaderAsync()
      {
        Task runTXParserTask = RunTXParserAsync();
        Task runBatcherTask = RunBatcherAsync();

        Task[] downloadTasks = new Task[COUNT_DOWNLOAD_TASKS];
        for (int i = 0; i < COUNT_DOWNLOAD_TASKS; i += 1)
        {
          var sessionBlockDownload = new SessionBlockDownload(this);
          Task runDownloadTask = UTXO.Network.RunSessionAsync(sessionBlockDownload);
        }
        await Task.WhenAll(downloadTasks);
      }
      async Task RunTXParserAsync()
      {

      }
      async Task RunBatcherAsync()
      {
        while (true)
        {
          UTXODownloadBatch downloadBatch = await DownloadBuffer.ReceiveAsync();

          if (downloadBatch.Index != DownloadBatcherIndex)
          {
            QueueDownloadBatch.Add(downloadBatch.Index, downloadBatch);
            continue;
          }

          while (true)
          {
            foreach (Block block in downloadBatch.Blocks)
            {
              FIFOBlocks.Enqueue(block);
              TXCountFIFO += block.TXCount;
            }

            while(TXCountFIFO > COUNT_TXS_IN_BATCH_FILE)
            {
              UTXOBatch batch = new UTXOBatch()
              {
                BatchIndex = BatchIndexMerge
              };

              BatchIndexMerge += 1;

              int tXCountBatch = 0;
              while (tXCountBatch < COUNT_TXS_IN_BATCH_FILE)
              {
                Block block = FIFOBlocks.Dequeue();
                batch.Blocks.Add(block);

                tXCountBatch += block.TXCount;
                TXCountFIFO -= block.TXCount;
              }

              BatchParserBuffer.Post(batch);
            }

            DownloadBatcherIndex += 1;

            if (!QueueDownloadBatch.TryGetValue(DownloadBatcherIndex, out downloadBatch))
            {
              break;
            }
          }
        }
      }
      bool TryGetDownloadBatch(out UTXODownloadBatch downloadBatch)
      {
        downloadBatch = new UTXODownloadBatch();
        
        lock (LOCK_DownloadIndex)
        {
          downloadBatch.Index = DownloadIndex;
          DownloadIndex += 1;

          Headerchain.ChainHeader headerBatchedLast
            = UTXO.Headerchain.ReadHeader(UTXO.HeaderHashBatchedLast, SHA256);

          if(headerBatchedLast.HeadersNext != null)
          {
            return false;
          }
          
          for (int i = 0; i < COUNT_BLOCKS_DOWNLOAD; i += 1)
          {
            downloadBatch.HeaderHashes[i] = headerBatchedLast.HeadersNext[0].GetHeaderHash(SHA256);
            headerBatchedLast = headerBatchedLast.HeadersNext[0];

            if (headerBatchedLast.HeadersNext == null)
            {
              UTXO.HeaderHashBatchedLast = downloadBatch.HeaderHashes[i];
              return true;
            }
          }

          UTXO.HeaderHashBatchedLast = downloadBatch.HeaderHashes[COUNT_BLOCKS_DOWNLOAD];
          return true;
        }
      }
    }
  }
}
