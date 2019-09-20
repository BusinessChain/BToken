using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Threading.Tasks.Dataflow;

using BToken.Networking;

namespace BToken.Chaining
{
  public partial class Blockchain
  {
    partial class GatewayHeaderchain : IGateway
    {
      const int INTERVAL_DOWNLOAD_CONTROLLER_MILLISECONDS = 30000;
      const int COUNT_NETWORK_PARSER_PARALLEL = 4;
      const int COUNT_NETWORK_SESSIONS = 1;

      Blockchain Blockchain;
      Network Network;
      
      readonly object LOCK_HeaderLoad = new object();
      Header HeaderLoadedLast;
      int IndexDownloadBatch;
      int StartBatchIndex;
      CancellationTokenSource CancellationLoader = new CancellationTokenSource();

      readonly object LOCK_IsSyncing = new object();
      bool IsSyncing;
      bool IsSyncingCompleted;

      BufferBlock<UTXOTable.UTXOBatch> ParserBuffer = new BufferBlock<UTXOTable.UTXOBatch>();
      public BufferBlock<UTXOTable.UTXOBatch> OutputBuffer = new BufferBlock<UTXOTable.UTXOBatch>();

      readonly object LOCK_CountBytesDownloaded = new object();
      BufferBlock<DownloadBatch> BatcherBuffer = new BufferBlock<DownloadBatch>();
      int DownloadBatcherIndex;
            
      enum SESSION_STATE { IDLE, DOWNLOADING, CANCELED };
      readonly object LOCK_DownloadSessions = new object();
      BufferBlock<DownloadBatch> DownloaderBuffer = new BufferBlock<DownloadBatch>();
      Dictionary<int, DownloadBatch> QueueDownloadBatch = new Dictionary<int, DownloadBatch>();

      Queue<Block> FIFOBlocks = new Queue<Block>();
      int TXCountFIFO;


      public GatewayHeaderchain(
        Blockchain blockchain,
        Network network, 
        Headerchain headerchain)
      {
        Blockchain = blockchain;
        Network = network;
      }
      


      const int COUNT_HEADER_SESSIONS = 4;

      public async Task Synchronize()
      {
        Task[] syncHeaderchainTasks = new Task[COUNT_HEADER_SESSIONS];

        for (int i = 0; i < COUNT_HEADER_SESSIONS; i += 1)
        {
          syncHeaderchainTasks[i] = 
            new SyncHeaderchainSession(this).Start();
        }

        await Task.WhenAll(syncHeaderchainTasks);

        await Task.Delay(3000);

        Console.WriteLine("Chain synced to hight {0}",
          Blockchain.Chain.GetHeight());
      }

           

      public void ReportInvalidBatch(DataBatch batch)
      {
        throw new NotImplementedException();
      }


      readonly object LOCK_LocatorHashes = new object();
      IEnumerable<byte[]> LocatorHashes;
      int IndexHeaderBatch;
      DataBatch HeaderBatchOld;
      TaskCompletionSource<object> SignalStartHeaderSyncSession =
        new TaskCompletionSource<object>();

      DataBatch CreateHeaderBatch()
      {
        int batchIndex;
        IEnumerable<byte[]> locatorHashes;

        lock (LOCK_IsSyncing)
        {
          batchIndex = IndexHeaderBatch;

          if (LocatorHashes == null)
          {
            LocatorHashes = Blockchain.GetLocatorHashes();
          }

          locatorHashes = LocatorHashes;
        }

        var headerBatch = new DataBatch(batchIndex);

        headerBatch.ItemBatchContainers.Add(
          new HeaderBatchContainer(
            headerBatch,
            locatorHashes));

        return headerBatch;
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
              Blockchain.UTXO.InputBuffer.Post(batch);

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
