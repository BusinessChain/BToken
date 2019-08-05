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
    partial class UTXONetworkLoader
    {
      const int COUNT_BLOCKS_DOWNLOAD_BATCH = 20;
      const int COUNT_NETWORK_PARSER_PARALLEL = 4;
      const int COUNT_DOWNLOAD_TASKS_PARALLEL = 8;

      const int INTERVAL_DOWNLOAD_CONTROLLER_MILLISECONDS = 60000;
      const int COUNTDOWN_CREATION_NEW_SESSION = 5;

      UTXO UTXO;
      Headerchain.ChainHeader StartHeader;
      int StartBatchIndex;
      CancellationTokenSource CancellationLoader = new CancellationTokenSource();

      BufferBlock<UTXOBatch> ParserBuffer = new BufferBlock<UTXOBatch>();
      public BufferBlock<UTXOBatch> OutputBuffer = new BufferBlock<UTXOBatch>();

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
      

      public UTXONetworkLoader(
        UTXO uTXO,
        Headerchain.ChainHeader startHeader,
        int startBatchIndex)
      {
        UTXO = uTXO;
        StartHeader = startHeader;
        StartBatchIndex = startBatchIndex;
        BatchIndexNextOutput = startBatchIndex;
      }

      public async Task RunAsync()
      {
        for (int i = 0; i < COUNT_NETWORK_PARSER_PARALLEL; i += 1)
        {
          Task parserTasks = RunParserAsync();
        }

        Task runBatcherTask = RunBatcherAsync(StartBatchIndex);

        CreateDownloadBatches(StartHeader);

        await RunDownloadControllerAsync();
      }

      async Task RunDownloadControllerAsync()
      {
        long countBytesDownloaded = 0;
        long countBytesDownloadedIntervalNew;
        long countBytesDownloadedIntervalOld = 0;
        int countDownloadSessionsNew = 0;
        int countDownloadSessionsOld = 0;
        int countDownCreationNewSession = 10;

        do
        {
          lock (LOCK_CountBytesDownloaded)
          {
            countBytesDownloadedIntervalNew = CountBytesDownloaded - countBytesDownloaded;
            countBytesDownloaded = CountBytesDownloaded;
          }

          lock (LOCK_DownloadSessions)
          {
            countDownloadSessionsNew = DownloadSessions.Count;

            foreach (SessionBlockDownload session in DownloadSessions)
            {
              Console.WriteLine("Session {0} has download rate {1} kByte/s",
                session.GetHashCode(),
                session.GetDownloadRatekiloBytePerSecond());
            }
          }

          Console.WriteLine("NetworkLoader downloaded {0} MB in total", countBytesDownloaded / 1000000);
          Console.WriteLine("download sessions running: {0}", countDownloadSessionsNew);
          Console.WriteLine("countBytesDownloadedIntervalNew: {0} MB", countBytesDownloadedIntervalNew / 1000000);
          Console.WriteLine("countDownloadSessionsNew: {0} ", countDownloadSessionsNew);
          Console.WriteLine("countBytesDownloadedIntervalOld: {0} MB", countBytesDownloadedIntervalOld / 1000000);
          Console.WriteLine("countDownloadSessionsOld: {0} ", countDownloadSessionsOld);

          if (countDownloadSessionsNew == 0)
          {
            for (int i = 0; i < COUNT_DOWNLOAD_TASKS_PARALLEL; i += 1)
            {
              StartDownloadSession();
            }
          }
          else
          {
            if (countBytesDownloadedIntervalNew < countBytesDownloadedIntervalOld * 0.9)
            {
              countDownCreationNewSession = COUNTDOWN_CREATION_NEW_SESSION;

              if (countDownloadSessionsNew > countDownloadSessionsOld)
              {
                CancelSessionSlowest();
              }
              else if (countDownloadSessionsNew < countDownloadSessionsOld)
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
              countDownCreationNewSession = COUNTDOWN_CREATION_NEW_SESSION;

              if (countDownloadSessionsNew > countDownloadSessionsOld)
              {
                StartDownloadSession();
              }
              else if (countDownloadSessionsNew < countDownloadSessionsOld)
              {
                CancelSessionSlowest();
              }
              else
              {
                StartDownloadSession();
              }
            }
            else
            {
              if (countDownCreationNewSession == 0)
              {
                StartDownloadSession();
                countDownCreationNewSession = COUNTDOWN_CREATION_NEW_SESSION;
              }
              else
              {
                countDownCreationNewSession -= 1;
              }
            }
          }


          lock (LOCK_DownloadSessions)
          {
            foreach (SessionBlockDownload session in DownloadSessions)
            {
              session.ResetStats();
            }
          }

          countBytesDownloadedIntervalOld = countBytesDownloadedIntervalNew;
          countDownloadSessionsOld = countDownloadSessionsNew;

          await Task.Delay(INTERVAL_DOWNLOAD_CONTROLLER_MILLISECONDS);
        } while (true);
      }

      void StartDownloadSession()
      {
        var session = new SessionBlockDownload(
          this,
          new UTXOParser(UTXO));

        Task downloadTask = UTXO.Network.RunSessionAsync(session);

        lock (LOCK_DownloadSessions)
        {
          DownloadSessions.Add(session);
        }

        Console.WriteLine("Start new session {0}", session.GetHashCode());
      }
      void CancelSessionSlowest()
      {
        SessionBlockDownload downloadSessionSlowest;

        lock (LOCK_DownloadSessions)
        {
          if (DownloadSessions.Count == 1)
          {
            return;
          }

          double minimumDownloadRate = DownloadSessions.Min(d => d.GetBytesDownloaded());

          downloadSessionSlowest = DownloadSessions.Find(
            d => d.GetBytesDownloaded() == minimumDownloadRate);
        }

        Console.WriteLine("Cancels slowest session {0} with download rate {1}",
          downloadSessionSlowest.GetHashCode(),
          downloadSessionSlowest.GetDownloadRatekiloBytePerSecond());

        downloadSessionSlowest.CancellationSession.Cancel();
      }

      async Task RunParserAsync()
      {
        UTXOParser parser = new UTXOParser(UTXO);

        while (true)
        {
          UTXOBatch batch = await ParserBuffer
            .ReceiveAsync(CancellationLoader.Token).ConfigureAwait(false);

          parser.ParseBatch(batch);

          PostToOutputBuffer(batch);

          Task archiveBatchTask = BlockArchiver.ArchiveBatchAsync(batch);          
        }
      }

      readonly object LOCK_OutputStage = new object();
      public int BatchIndexNextOutput;
      Dictionary<int, UTXOBatch> OutputQueue = new Dictionary<int, UTXOBatch>();

      public void PostToOutputBuffer(UTXOBatch batch)
      {
        lock (LOCK_OutputStage)
        {
          if (batch.BatchIndex != BatchIndexNextOutput)
          {
            OutputQueue.Add(batch.BatchIndex, batch);
          }
          else
          {
            while (true)
            {
              UTXO.Merger.Buffer.Post(batch);

              BatchIndexNextOutput += 1;

              if (OutputQueue.TryGetValue(BatchIndexNextOutput, out batch))
              {
                OutputQueue.Remove(BatchIndexNextOutput);
              }
              else
              {
                break;
              }
            }
          }
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
              .ReceiveAsync(CancellationLoader.Token).ConfigureAwait(false);
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

            if (downloadBatch.IsCancellationBatch)
            {
              UTXOBatch batch = new UTXOBatch()
              {
                BatchIndex = batchIndex++,
              };

              do
              {
                Block block = FIFOBlocks.Dequeue();
                batch.Blocks.Add(block);
                batch.TXCount += block.TXCount;

                if (FIFOBlocks.Count == 0)
                {
                  batch.IsCancellationBatch = true;
                  ParserBuffer.Post(batch);
                  return;
                }

                Block nextBlock = FIFOBlocks.Peek();

                if (batch.TXCount + nextBlock.TXCount > COUNT_TXS_IN_BATCH_FILE)
                {
                  ParserBuffer.Post(batch);

                  batch = new UTXOBatch()
                  {
                    BatchIndex = batchIndex++,
                  };
                }
              } while (true);
            }

            while (TXCountFIFO >= COUNT_TXS_IN_BATCH_FILE)
            {
              UTXOBatch batch = new UTXOBatch()
              {
                BatchIndex = batchIndex++,
              };

              do
              {
                Block block = FIFOBlocks.Dequeue();
                batch.Blocks.Add(block);
                batch.TXCount += block.TXCount;
                TXCountFIFO -= block.TXCount;

                if (FIFOBlocks.Count == 0)
                {
                  break;
                }

              } while (batch.TXCount + FIFOBlocks.Peek().TXCount <= COUNT_TXS_IN_BATCH_FILE);

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
      void PostDownload(UTXODownloadBatch downloadBatch)
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
