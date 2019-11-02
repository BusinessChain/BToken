using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Threading.Tasks.Dataflow;
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
        public int CountBlocks = COUNT_BLOCKS_DOWNLOADBATCH_INIT;
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
                CountBlocks))
              {
                StopwatchDownload.Restart();

                await Channel.StartBlockDownloadAsync(UTXOBatch);
                
                StopwatchDownload.Stop();

                await Synchronizer.InputBuffer.SendAsync(UTXOBatch);

                CalculateNewCountBlocks();
              }

              Channel.Release();

              return;
            }
            catch (Exception ex)
            {
              Console.WriteLine("Exception {0} in block download: \n{1}" +
                "batch {2} queued",
                ex.GetType().Name,
                ex.Message,
                UTXOBatch.Index);

              Synchronizer.QueueBatchesCanceled.Enqueue(UTXOBatch);

              Channel.Dispose();
              Channel = null;

              CountBlocks = COUNT_BLOCKS_DOWNLOADBATCH_INIT;
            }
          }
        }
                

        void CalculateNewCountBlocks()
        {
          const float safetyFactorTimeout = 10;
          const float marginFactorResetCountBlocksDownload = 5;

          float ratioTimeoutToDownloadTime = TIMEOUT_BLOCKDOWNLOAD_MILLISECONDS
            / (1 + StopwatchDownload.ElapsedMilliseconds);

          if (ratioTimeoutToDownloadTime > safetyFactorTimeout)
          {
            CountBlocks += 1;
          }
          else if (ratioTimeoutToDownloadTime < marginFactorResetCountBlocksDownload &&
            CountBlocks > COUNT_BLOCKS_DOWNLOADBATCH_INIT)
          {
            CountBlocks = COUNT_BLOCKS_DOWNLOADBATCH_INIT;
          }
          else if (CountBlocks > 1)
          {
            CountBlocks -= 1;
          }
        }
      }
    }
  }
}