using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Threading.Tasks.Dataflow;
using System.Security.Cryptography;
using System.Diagnostics;


namespace BToken.Chaining
{
  partial class UTXOTable
  {
    public partial class UTXOSynchronizer : DataSynchronizer
    {
      class SyncUTXOSession
      {
        const int COUNT_BLOCKS_DOWNLOADBATCH_INIT = 1;
        const int INTERVAL_DOWNLOAD_CONTROLLER_MILLISECONDS = 30000;

        const int TIMEOUT_GETHEADERS_MILLISECONDS = 10000;
        const int TIMEOUT_BLOCKDOWNLOAD_MILLISECONDS = 20000;

        UTXOSynchronizer Synchronizer;
        UTXOChannel Channel;
        



        public SyncUTXOSession(
          UTXOSynchronizer synchronizer)
        {
          Synchronizer = synchronizer;
        }

        public SyncUTXOSession(
          UTXOSynchronizer synchronizer,
          UTXOChannel channel)
        {
          Synchronizer = synchronizer;
          Channel = channel;
        }



        Stopwatch StopwatchDownload = new Stopwatch();
        public int CountBlocksDownloadBatch = COUNT_BLOCKS_DOWNLOADBATCH_INIT;
        DataBatch UTXOBatch;

        public async Task Start()
        {
          while (true)
          {
            if(Channel == null)
            {
              Channel = await Synchronizer.RequestChannel();
            }

            try
            {
              while (Synchronizer.TryGetBatch(
                out UTXOBatch,
                CountBlocksDownloadBatch))
              {
                await StartBlockDownloadAsync();
              }

              Synchronizer.ReturnChannel(Channel);

              return;
            }
            catch (Exception ex)
            {
              Console.WriteLine("Exception in block download: \n{0}" +
                "batch {1} queued",
                ex.Message,
                UTXOBatch.Index);

              Synchronizer.QueueBatchesCanceled.Enqueue(UTXOBatch);

              Synchronizer.DisposeChannel(Channel);

              CountBlocksDownloadBatch = COUNT_BLOCKS_DOWNLOADBATCH_INIT;
            }
          }
        }

        
        async Task StartBlockDownloadAsync()
        {
          StopwatchDownload.Restart();

          List<byte[]> hashesRequested = new List<byte[]>();

          foreach (BlockContainer blockBatchContainer in
            UTXOBatch.DataContainers)
          {
            if (blockBatchContainer.Buffer == null)
            {
              hashesRequested.Add(
                blockBatchContainer.Header.HeaderHash);
            }
          }

          await Channel.RequestBlocks(hashesRequested);

          var cancellationDownloadBlocks =
            new CancellationTokenSource(TIMEOUT_BLOCKDOWNLOAD_MILLISECONDS);
          
          foreach (BlockContainer blockBatchContainer in
            UTXOBatch.DataContainers)
          {
            if (blockBatchContainer.Buffer != null)
            {
              continue;
            }

            blockBatchContainer.Buffer = await Channel
              .ReceiveBlock(cancellationDownloadBlocks.Token)
              .ConfigureAwait(false);

            blockBatchContainer.TryParse();
            UTXOBatch.CountItems += blockBatchContainer.CountItems;
          }

          await Synchronizer.InputBuffer.SendAsync(UTXOBatch);
          
          StopwatchDownload.Stop();

          CalculateNewCountBlocks();
        }

        void CalculateNewCountBlocks()
        {
          const float safetyFactorTimeout = 10;
          const float marginFactorResetCountBlocksDownload = 5;

          float ratioTimeoutToDownloadTime = TIMEOUT_BLOCKDOWNLOAD_MILLISECONDS
            / (1 + StopwatchDownload.ElapsedMilliseconds);

          if (ratioTimeoutToDownloadTime > safetyFactorTimeout)
          {
            CountBlocksDownloadBatch += 1;
          }
          else if (ratioTimeoutToDownloadTime < marginFactorResetCountBlocksDownload &&
            CountBlocksDownloadBatch > COUNT_BLOCKS_DOWNLOADBATCH_INIT)
          {
            CountBlocksDownloadBatch = COUNT_BLOCKS_DOWNLOADBATCH_INIT;
          }
          else if (CountBlocksDownloadBatch > 1)
          {
            CountBlocksDownloadBatch -= 1;
          }
        }
      }
    }
  }
}