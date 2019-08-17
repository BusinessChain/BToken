using System;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Threading.Tasks.Dataflow;

using BToken.Chaining;
using BToken.Networking;

namespace BToken.Accounting
{
  public partial class UTXO
  {
    partial class UTXONetworkLoader
    {
      const int INTERVAL_DOWNLOAD_CONTROLLER_MILLISECONDS = 30000;
      const int COUNT_NETWORK_PARSER_PARALLEL = 4;
      const int COUNT_DOWNLOAD_SESSIONS = 4;

      UTXO UTXO;

      readonly object LOCK_HeaderLoad = new object();
      Headerchain.ChainHeader HeaderLoadedLast;
      int IndexDownloadBatch;
      int StartBatchIndex;
      CancellationTokenSource CancellationLoader = new CancellationTokenSource();

      BufferBlock<UTXOBatch> ParserBuffer = new BufferBlock<UTXOBatch>();
      public BufferBlock<UTXOBatch> OutputBuffer = new BufferBlock<UTXOBatch>();

      readonly object LOCK_CountBytesDownloaded = new object();
      BufferBlock<DownloadBatch> BatcherBuffer
        = new BufferBlock<DownloadBatch>();
      int DownloadBatcherIndex;

      ConcurrentQueue<DownloadBatch> QueueBatchesCanceled
        = new ConcurrentQueue<DownloadBatch>();

      enum SESSION_STATE { IDLE, DOWNLOADING, CANCELED };
      readonly object LOCK_DownloadSessions = new object();
      List<SessionBlockDownload> DownloadSessions = new List<SessionBlockDownload>();
      BufferBlock<DownloadBatch> DownloaderBuffer = new BufferBlock<DownloadBatch>();
      Dictionary<int, DownloadBatch> QueueDownloadBatch = new Dictionary<int, DownloadBatch>();

      Queue<Block> FIFOBlocks = new Queue<Block>();
      int TXCountFIFO;

      long CountBytesDownloaded;
      

      public UTXONetworkLoader(
        UTXO uTXO,
        Headerchain.ChainHeader headerPostedToMergerLast,
        int startBatchIndex)
      {
        UTXO = uTXO;
        HeaderLoadedLast = headerPostedToMergerLast;
        StartBatchIndex = startBatchIndex;
        BatchIndexNextOutput = startBatchIndex;

      }

      public void Start()
      {
        for (int i = 0; i < COUNT_NETWORK_PARSER_PARALLEL; i += 1)
        {
          StartParserAsync();
        }

        StartBatcherAsync();

        StartSessionControlAsync();
      }
      async Task StartSessionControlAsync()
      {
        lock (LOCK_DownloadSessions)
        {
          for (int i = 0; i < COUNT_DOWNLOAD_SESSIONS; i += 1)
          {
            var session = new SessionBlockDownload(
              UTXO.Network,
              this,
              new UTXOParser(UTXO));

            DownloadSessions.Add(session);

            session.StartAsync();
          }
        }

        while (true)
        {
          await Task.Delay(INTERVAL_DOWNLOAD_CONTROLLER_MILLISECONDS);

          Console.WriteLine();
          Console.WriteLine("Download session metrics:");

          DownloadSessions.ForEach(s => s.PrintDownloadMetrics(INTERVAL_DOWNLOAD_CONTROLLER_MILLISECONDS));
          Console.WriteLine();
        }
      }

      bool TryGetDownloadBatch(out DownloadBatch downloadBatch, int countHeaders)
      {
        if(QueueBatchesCanceled.TryDequeue(out downloadBatch))
        {
          return true;
        }

        lock(LOCK_HeaderLoad)
        {
          if (HeaderLoadedLast.HeadersNext == null)
          {
            downloadBatch = null;
            return false;
          }

          downloadBatch = new DownloadBatch(IndexDownloadBatch++);

          var header = HeaderLoadedLast;

          for (int i = 0; i < countHeaders; i += 1)
          {
            header = header.HeadersNext[0];

            downloadBatch.Headers.Add(header);

            if (header.HeadersNext == null)
            {
              downloadBatch.IsAtTipOfChain = true;
              break;
            }
          }

          HeaderLoadedLast = header;
          return true;
        }
      }
      
      async Task StartParserAsync()
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
      

      DownloadBatch DownloadBatch;
      UTXOBatch UTXOBatch;
      async Task StartBatcherAsync()
      {
        UTXOBatch = new UTXOBatch()
        {
          BatchIndex = StartBatchIndex,
        };

        while (true)
        {
          DownloadBatch = await BatcherBuffer.ReceiveAsync().ConfigureAwait(false);

          if (DownloadBatch.Index != DownloadBatcherIndex)
          {
            QueueDownloadBatch.Add(DownloadBatch.Index, DownloadBatch);
            continue;
          }
                   
          do
          {
            foreach (Block block in DownloadBatch.Blocks)
            {
              if (block.TXCount > COUNT_TXS_IN_BATCH_FILE)
              {
                throw new InvalidOperationException(
                  string.Format("block {0} has more than COUNT_TXS_IN_BATCH_FILE = {1} transactions",
                  FIFOBlocks.Peek().HeaderHash.ToHexString(),
                  COUNT_TXS_IN_BATCH_FILE));
              }

              FIFOBlocks.Enqueue(block);
              TXCountFIFO += block.TXCount;
            }

            DequeueBatches();
            
            DownloadBatcherIndex += 1;

            if (QueueDownloadBatch.TryGetValue(DownloadBatcherIndex, out DownloadBatch))
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
      void DequeueBatches()
      {
        if (UTXOBatch.TXCount + FIFOBlocks.Peek().TXCount > COUNT_TXS_IN_BATCH_FILE)
        {
          PostBatch();
        }

        while (true)
        {
          do
          {
            Block block = FIFOBlocks.Dequeue();
            UTXOBatch.Blocks.Add(block);
            UTXOBatch.TXCount += block.TXCount;
            TXCountFIFO -= block.TXCount;

            if (FIFOBlocks.Count == 0)
            {
              if (DownloadBatch.IsAtTipOfChain)
              {
                PostBatch();
              }

              return;
            }

            if (UTXOBatch.TXCount + FIFOBlocks.Peek().TXCount > COUNT_TXS_IN_BATCH_FILE)
            {
              PostBatch();
              break;
            }
          } while (true);
        }
      }
      void PostBatch()
      {
        ParserBuffer.Post(UTXOBatch);

        UTXOBatch = new UTXOBatch()
        {
          BatchIndex = UTXOBatch.BatchIndex + 1,
        };
      }

    }
  }
}
