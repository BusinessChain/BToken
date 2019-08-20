using System;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Threading.Tasks.Dataflow;

namespace BToken.Chaining
{
  public partial class Blockchain
  {
    partial class BlockchainNetworkGateway
    {
      const int INTERVAL_DOWNLOAD_CONTROLLER_MILLISECONDS = 30000;
      const int COUNT_NETWORK_PARSER_PARALLEL = 4;
      const int COUNT_DOWNLOAD_SESSIONS = 8;

      Blockchain Blockchain;

      readonly object LOCK_HeaderLoad = new object();
      Header HeaderLoadedLast;
      int IndexDownloadBatch;
      int StartBatchIndex;
      CancellationTokenSource CancellationLoader = new CancellationTokenSource();

      BufferBlock<UTXOTable.UTXOBatch> ParserBuffer = new BufferBlock<UTXOTable.UTXOBatch>();
      public BufferBlock<UTXOTable.UTXOBatch> OutputBuffer = new BufferBlock<UTXOTable.UTXOBatch>();

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


      public BlockchainNetworkGateway(Blockchain blockchain)
      {
        Blockchain = blockchain;
      }

      public void Start()
      {
        HeaderLoadedLast = Blockchain.ArchiveLoader.HeaderPostedToMergerLast;
        StartBatchIndex = Blockchain.ArchiveLoader.BatchIndexLoad;
        BatchIndexNextOutput = Blockchain.ArchiveLoader.BatchIndexLoad;

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
              Blockchain.Network,
              this,
              new BlockParser(Blockchain));

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
        var parser = new BlockParser(Blockchain);

        while (true)
        {
          UTXOTable.UTXOBatch batch = await ParserBuffer
            .ReceiveAsync(CancellationLoader.Token).ConfigureAwait(false);

          parser.ParseBatch(batch);

          PostToOutputBuffer(batch);

          Task archiveBatchTask = BlockArchiver.ArchiveBatchAsync(batch);          
        }
      }

      readonly object LOCK_OutputStage = new object();
      public int BatchIndexNextOutput;
      Dictionary<int, UTXOTable.UTXOBatch> OutputQueue = new Dictionary<int, UTXOTable.UTXOBatch>();
      public void PostToOutputBuffer(UTXOTable.UTXOBatch batch)
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
              Blockchain.UTXO.BatchBuffer.Post(batch);

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
      UTXOTable.UTXOBatch UTXOBatch;
      async Task StartBatcherAsync()
      {
        UTXOBatch = new UTXOTable.UTXOBatch()
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

        UTXOBatch = new UTXOTable.UTXOBatch()
        {
          BatchIndex = UTXOBatch.BatchIndex + 1,
        };
      }

    }
  }
}
