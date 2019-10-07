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
        

        
        public SyncUTXOSession(GatewayUTXO gateway)
        {
          Gateway = gateway;
          SHA256 = SHA256.Create();
        }



        Stopwatch StopwatchDownload = new Stopwatch();
        public int CountBlocksDownloadBatch = COUNT_BLOCKS_DOWNLOADBATCH_INIT;
        DataBatch UTXOBatch;

        public async Task Start()
        {
          while(true)
          {
            Channel = await Gateway.Network.RequestChannel();

            Console.WriteLine("sync UTXO session {0} aquired channel {1}.",
              GetHashCode(),
              Channel.GetIdentification());

            try
            {
              while (Gateway.TryGetDownloadBatch(
                out UTXOBatch,
                CountBlocksDownloadBatch))
              {
                await StartBlockDownloadAsync();
              }

              Console.WriteLine("utxo Session {0} returns {1}", 
                GetHashCode(),
                Channel.GetIdentification());

              Gateway.Network.ReturnChannel(Channel);

              return;
            }
            catch (Exception ex)
            {
              Console.WriteLine("Exception in block download: \n{0}" +
                "batch {1} queued",
                ex.Message,
                UTXOBatch.Index);

              Gateway.QueueBatchesCanceled.Enqueue(UTXOBatch);

              Gateway.Network.DisposeChannel(Channel);

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
              hashesRequested.Add(blockBatchContainer.Header.HeaderHash);
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

          }

          await Gateway.Blockchain.UTXODataPipe.InputBuffer.SendAsync(UTXOBatch);
          
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