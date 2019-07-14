using System;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Threading.Tasks.Dataflow;

using BToken.Chaining;

namespace BToken.Accounting
{
  public partial class UTXO
  {
    partial class UTXOBuilder
    {
      partial class UTXONetworkLoader
      {
        const int COUNT_BLOCKS_DOWNLOAD_BATCH = 10;
        const int COUNT_NETWORK_PARSER_PARALLEL = 4;
        const int COUNT_DOWNLOAD_TASKS_PARALLEL = 4;

        const int INTERVAL_DOWNLOAD_CONTROLLER_MILLISECONDS = 100000;

        UTXOBuilder Builder;
        CancellationToken CancellationToken;

        BufferBlock<UTXOBatch> ParserBuffer = new BufferBlock<UTXOBatch>();

        readonly object LOCK_CountBytesDownloaded = new object();
        BufferBlock<UTXODownloadBatch> BatcherBuffer
          = new BufferBlock<UTXODownloadBatch>();
        int DownloadBatcherIndex;

        ConcurrentQueue<UTXODownloadBatch> QueueDownloadBatchesCanceled
          = new ConcurrentQueue<UTXODownloadBatch>();


        readonly object LOCK_DownloadSessions = new object();
        List<SessionBlockDownload> DownloadSessions = new List<SessionBlockDownload>();
        BufferBlock<UTXODownloadBatch> DownloaderBuffer
          = new BufferBlock<UTXODownloadBatch>();
        Dictionary<int, UTXODownloadBatch> QueueDownloadBatch
          = new Dictionary<int, UTXODownloadBatch>();

        Queue<Block> FIFOBlocks = new Queue<Block>();
        int TXCountFIFO;
        
        long CountBytesDownloaded;



        public UTXONetworkLoader(UTXOBuilder builder, CancellationToken cancellationToken)
        {
          Builder = builder;
          CancellationToken = cancellationToken;
        }

        public void Start(Headerchain.ChainHeader startHeader, int batchIndex)
        {
          for (int i = 0; i < COUNT_NETWORK_PARSER_PARALLEL; i += 1)
          {
            Task parserTasks = RunTXParserAsync();
          }

          Task runBatcherTask = RunBatcherAsync(batchIndex);
          
          Task downloadControllerTask = RunDownloadControllerAsync();

          CreateDownloadBatches(startHeader);
        }

        void StartDownloadSession()
        {
          var session = new SessionBlockDownload(
            this,
            new UTXOParser(Builder.UTXO));

          Task downloadTask = Builder.UTXO.Network.RunSessionAsync(session);

          Console.WriteLine("Start download session {0}", session.GetHashCode());

          lock (LOCK_DownloadSessions)
          {
            DownloadSessions.Add(session);
          }

        }
        async Task RunDownloadControllerAsync()
        {
          for (int i = 0; i < COUNT_DOWNLOAD_TASKS_PARALLEL; i += 1)
          {
            StartDownloadSession();
          }
                    
          long countBytesDownloaded = 0;
          long countBytesDownloadedIntervalNew;
          long countBytesDownloadedIntervalOld = 0;
          int countDownloadSessions = 0;
          int countDownloadSessionsOld = 0;
          int countDownCreationNewSession = 10;

          do
          {
            await Task.Delay(INTERVAL_DOWNLOAD_CONTROLLER_MILLISECONDS);

            lock (LOCK_CountBytesDownloaded)
            {
              countBytesDownloadedIntervalNew = CountBytesDownloaded - countBytesDownloaded;
              countBytesDownloaded = CountBytesDownloaded;
            }

            Console.WriteLine("NetworkLoader downloaded {0} MB in total", countBytesDownloaded/1000000);

            lock (LOCK_DownloadSessions)
            {
              countDownloadSessions = DownloadSessions.Count;
            }

            Console.WriteLine("download sessions running: {0}", countDownloadSessions);

            Console.WriteLine("countBytesDownloadedIntervalNew: {0} MB", countBytesDownloadedIntervalNew/1000000);
            Console.WriteLine("countBytesDownloadedIntervalOld: {0} MB", countBytesDownloadedIntervalOld/1000000);

            if (countBytesDownloadedIntervalNew < countBytesDownloadedIntervalOld * 0.9)
            {
              if (countDownloadSessions > countDownloadSessionsOld)
              {
                CancelSessionSlowest();
              }
              else if (countDownloadSessions < countDownloadSessionsOld)
              {
                StartDownloadSession();
              }
              else
              {
                CancelSessionSlowest();
              }
            }
            else if (countBytesDownloadedIntervalNew > countBytesDownloadedIntervalOld * 1.1)
            {
              if (countDownloadSessions > countDownloadSessionsOld)
              {
                StartDownloadSession();
              }
              else if(countDownloadSessions < countDownloadSessionsOld)
              {
                CancelSessionSlowest();
              }
              else
              {
                StartDownloadSession();
              }
            }
            {
              if(countDownCreationNewSession == 0)
              {
                StartDownloadSession();
                countDownCreationNewSession = 5;
              }

              countDownCreationNewSession -= 1;
            }

            countBytesDownloadedIntervalOld = countBytesDownloadedIntervalNew;
            countDownloadSessionsOld = countDownloadSessions;

          } while (true);
        }

        void CancelSessionSlowest()
        {
          SessionBlockDownload downloadSessionSlowest;

          lock (LOCK_DownloadSessions)
          {
            if(DownloadSessions.Count == 1)
            {
              return;
            }

            double minimumDownloadRate = DownloadSessions.Min(d => d.GetBytesDownloaded());

            downloadSessionSlowest = DownloadSessions.Find(
              d => d.GetBytesDownloaded() == minimumDownloadRate);
          }

          Console.WriteLine("Cancels slowest session {0}", downloadSessionSlowest.GetHashCode());

          downloadSessionSlowest.CancellationSession.Cancel();
        }
        void CancelSession(SessionBlockDownload downloadSession)
        {
          QueueDownloadBatchesCanceled.Enqueue(downloadSession.DownloadBatch);

          lock(LOCK_DownloadSessions)
          {
            DownloadSessions.Remove(downloadSession);
          }
        }


        async Task RunTXParserAsync()
        {
          UTXOParser parser = new UTXOParser(Builder.UTXO);

          while (true)
          {
            UTXOBatch batch = await ParserBuffer
              .ReceiveAsync(CancellationToken).ConfigureAwait(false);

            parser.ParseBatch(batch);

            Task archiveBatchTask = BlockArchiver.ArchiveBatchAsync(batch);

            Builder.Merger.Buffer.Post(batch);
          }
        }
        async Task RunBatcherAsync(int batchIndex)
        {
          UTXODownloadBatch downloadBatch;

          while (true)
          {
            try
            {
              downloadBatch = await BatcherBuffer
                .ReceiveAsync(CancellationToken).ConfigureAwait(false);
            }
            catch (TaskCanceledException)
            {
              return;
            }

            if (downloadBatch.BatchIndex != DownloadBatcherIndex)
            {
              QueueDownloadBatch.Add(downloadBatch.BatchIndex, downloadBatch);
              continue;
            }

            do
            {
              foreach (Block block in downloadBatch.Blocks)
              {
                FIFOBlocks.Enqueue(block);
                TXCountFIFO += block.TXCount;
              }

              while (
                TXCountFIFO > COUNT_TXS_IN_BATCH_FILE ||
                (downloadBatch.IsCancellationBatch && TXCountFIFO > 0))
              {
                UTXOBatch batch = new UTXOBatch()
                {
                  BatchIndex = batchIndex++,
                  IsCancellationBatch = downloadBatch.IsCancellationBatch
                };

                int tXCountBatch = 0;
                while (
                  tXCountBatch < COUNT_TXS_IN_BATCH_FILE ||
                  (batch.IsCancellationBatch && TXCountFIFO > 0))
                {
                  Block block = FIFOBlocks.Dequeue();
                  batch.Blocks.Add(block);

                  tXCountBatch += block.TXCount;
                  TXCountFIFO -= block.TXCount;
                }

                ParserBuffer.Post(batch);
              }

              DownloadBatcherIndex += 1;

              if (QueueDownloadBatch.TryGetValue(DownloadBatcherIndex, out downloadBatch))
              {
                QueueDownloadBatch.Remove(DownloadBatcherIndex);
              }
              else
              {
                break;
              }

            } while (true);
          }
        }
        void PostDownloadToBatcher(UTXODownloadBatch downloadBatch)
        {
          lock (LOCK_CountBytesDownloaded)
          {
            CountBytesDownloaded += downloadBatch.BytesDownloaded;
          }

          BatcherBuffer.Post(downloadBatch);
        }

        void CreateDownloadBatches(Headerchain.ChainHeader header)
        {
          int indexDownloadBatch = 0;

          while (true)
          {
            var downloadBatch = new UTXODownloadBatch(indexDownloadBatch++);

            for (int i = 0; i < COUNT_BLOCKS_DOWNLOAD_BATCH; i += 1)
            {
              downloadBatch.Headers.Add(header);

              if (header.HeadersNext == null)
              {
                downloadBatch.IsCancellationBatch = true;
                DownloaderBuffer.Post(downloadBatch);
                return;
              }

              header = header.HeadersNext[0];
            }

            DownloaderBuffer.Post(downloadBatch);
          }
        }
      }
    }
  }
}
