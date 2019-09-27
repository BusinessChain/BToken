using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Threading.Tasks.Dataflow;
using System.Security.Cryptography;
using System.Diagnostics;

using BToken.Networking;


namespace BToken.Chaining
{
  public partial class Blockchain
  {
    partial class GatewayUTXO
    {
      class SyncUTXOSession
      {
        public INetworkChannel Channel;

        const int COUNT_BLOCKS_DOWNLOADBATCH_INIT = 1;
        const int INTERVAL_DOWNLOAD_CONTROLLER_MILLISECONDS = 30000;

        const int TIMEOUT_GETHEADERS_MILLISECONDS = 10000;
        const int TIMEOUT_BLOCKDOWNLOAD_MILLISECONDS = 20000;

        GatewayUTXO Gateway;
        SHA256 SHA256;
        
        Stopwatch StopwatchDownload = new Stopwatch();
        public int CountBlocksDownloadBatch = COUNT_BLOCKS_DOWNLOADBATCH_INIT;
        DataBatch UTXOBatch;

        readonly object LOCK_BytesDownloaded = new object();
        long BytesDownloaded;

        public DateTimeOffset TimeStartChannelInterval;

        public SyncUTXOSession(GatewayUTXO gateway)
        {
          Gateway = gateway;
          SHA256 = SHA256.Create();
        }
        

        
        public async Task Start()
        {
          while(true)
          {
            Console.WriteLine("sync UTXO session {0} requests channel.", 
              GetHashCode());

            Channel = await Gateway.Network.RequestChannelAsync();

            Console.WriteLine("sync UTXO session {0} aquired channel {1}.",
              GetHashCode(),
              Channel.GetIdentification());
            
            TimeStartChannelInterval = DateTimeOffset.UtcNow;
            BytesDownloaded = 0;

            try
            {
              while (Gateway.TryGetDownloadBatch(
                out UTXOBatch,
                CountBlocksDownloadBatch))
              {
                await StartBlockDownloadAsync();
              }

              return;
            }
            catch (Exception ex)
            {
              Console.WriteLine("Exception in block download: \n{0}" +
                "batch {1} queued",
                ex.Message,
                UTXOBatch.Index);

              Gateway.QueueBatchesCanceled.Enqueue(UTXOBatch);

              CountBlocksDownloadBatch = COUNT_BLOCKS_DOWNLOADBATCH_INIT;
            }
          }
        }


        async Task RunListenerAsync()
        {
          while(true)
          {
            await Task.Delay(INTERVAL_DOWNLOAD_CONTROLLER_MILLISECONDS);
            Console.WriteLine("session {0} listening.", GetHashCode());
          }
        }

        async Task StartBlockDownloadAsync()
        {
          StopwatchDownload.Restart();
                           
          List<byte[]> hashesRequested = new List<byte[]>();

          foreach (UTXOTable.BlockBatchContainer blockBatchContainer in
            UTXOBatch.ItemBatchContainers)
          {
            if(blockBatchContainer.Buffer == null)
            {
              hashesRequested.Add(blockBatchContainer.HeaderRoot.HeaderHash);
            }
          }

          var cancellationDownloadBlocks =
            new CancellationTokenSource(TIMEOUT_BLOCKDOWNLOAD_MILLISECONDS);
          
          await Channel.RequestBlocksAsync(hashesRequested);

          foreach (UTXOTable.BlockBatchContainer blockBatchContainer in
            UTXOBatch.ItemBatchContainers)
          {
            if (blockBatchContainer.Buffer != null)
            {
              continue;
            }

            blockBatchContainer.Buffer = await Channel
              .ReceiveBlockAsync(cancellationDownloadBlocks.Token)
              .ConfigureAwait(false);
            
            blockBatchContainer.Parse();
            UTXOBatch.CountItems += blockBatchContainer.CountItems;

            lock (LOCK_BytesDownloaded)
            {
              BytesDownloaded += blockBatchContainer.Buffer.Length;
            }
          }

          await Gateway.Blockchain.UTXODataPipe.InputBuffer.SendAsync(UTXOBatch);

          Console.WriteLine("Downloaded batch {0} with {1} blocks and {2} txs", 
            UTXOBatch.Index,
            UTXOBatch.ItemBatchContainers.Count,
            UTXOBatch.CountItems);

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
          else if(CountBlocksDownloadBatch > 1)
          {
            CountBlocksDownloadBatch -= 1;
          }
        }
      }
    }
  }
}