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
    partial class GatewayBlockchainNetwork
    {
      class NetworkSession
      {
        public INetworkChannel Channel;

        const int COUNT_BLOCKS_DOWNLOADBATCH_INIT = 1;
        const int INTERVAL_DOWNLOAD_CONTROLLER_MILLISECONDS = 30000;

        const int TIMEOUT_GETHEADERS_MILLISECONDS = 10000;
        const int TIMEOUT_BLOCKDOWNLOAD_MILLISECONDS = 20000;

        GatewayBlockchainNetwork Gateway;
        BlockParser Parser;
        SHA256 SHA256;

        public SESSION_STATE State = SESSION_STATE.IDLE;
        readonly object LOCK_State = new object();
        bool IsSyncing;

        Stopwatch StopwatchDownload = new Stopwatch();
        public int CountBlocksDownloadBatch = COUNT_BLOCKS_DOWNLOADBATCH_INIT;
        public DownloadBatch DownloadBatch;
        public CancellationTokenSource CancellationSession;

        readonly object LOCK_BytesDownloaded = new object();
        long BytesDownloaded;
        int CountBlocksDownloaded;

        public DateTimeOffset TimeStartChannelInterval;

        public NetworkSession(GatewayBlockchainNetwork gateway, BlockParser parser)
        {
          Gateway = gateway;
          Parser = parser;
          SHA256 = SHA256.Create();

          CancellationSession = CancellationTokenSource.CreateLinkedTokenSource(
            Gateway.CancellationLoader.Token);
        }
        
        public async Task StartAsync()
        {
          while(true)
          {
            Channel = await Gateway.Network.RequestChannelAsync();

            try
            {
              await SynchronizeChainAsync();

              Console.WriteLine("session {0} ends chain syncing.", GetHashCode());
            }
            catch (Exception ex)
            {
              Console.WriteLine("Exception in chain syncing: '{0}' in session {1} with channel {2}", 
                ex.Message,
                GetHashCode(),
                Channel == null ? "" : Channel.GetIdentification());
                           
              lock (Gateway.LOCK_IsSyncing)
              {
                if (IsSyncing)
                {
                  IsSyncing = false;
                  Gateway.IsSyncing = false;
                }

                if (!Gateway.IsSyncingCompleted)
                {
                  continue;
                }
              }
            }

            try
            {
              await SynchronizeUTXOAsync();
              //await RunListenerAsync();
            }
            catch (Exception ex)
            {
              Console.WriteLine("Exception in utxo syncing: '{0}' in session {1} with channel {2}",
                ex.Message,
                GetHashCode(),
                Channel == null ? "" : Channel.GetIdentification());
            }

          }
        }


        HeaderBatch HeaderBatchOld = null;
        HeaderBatch HeaderBatch = null;
        int HeaderBatchIndex = 0;

        async Task SynchronizeChainAsync()
        {
          CancellationTokenSource cancellation = new CancellationTokenSource(TIMEOUT_GETHEADERS_MILLISECONDS);

          IEnumerable<byte[]> locatorHashes = Gateway.GetLocatorHashes();

          byte[] headerBytes = await Channel.GetHeadersAsync(
            locatorHashes,
            cancellation.Token);

          List<Header> headers = ParseHeaders(headerBytes);
          
          HeaderBatch = new HeaderBatch(
            headers, 
            headerBytes,
            HeaderBatchIndex++);

          while(true)
          {
            lock (Gateway.LOCK_IsSyncing)
            {
              if(Gateway.IsSyncingCompleted)
              {
                return;
              }

              if (!Gateway.IsSyncing)
              {
                Gateway.IsSyncing = true;
                IsSyncing = true;
                break;
              }
            }

            await Task.Delay(3000);
          }

          Console.WriteLine("session {0} enters header syncing", GetHashCode());

          while (headers.Any())
          {
            if (HeaderBatchOld != null)
            {
              await Gateway.HeaderBatchDataPipe.InputBuffer.SendAsync(HeaderBatchOld);
            }

            HeaderBatchOld = HeaderBatch;
            
            locatorHashes = Gateway.SetLocatorHashes(
              new List<byte[]> { headers.Last().HeaderHash });

            cancellation.CancelAfter(TIMEOUT_GETHEADERS_MILLISECONDS);
            headerBytes = await Channel.GetHeadersAsync(
              locatorHashes,
              cancellation.Token);

            headers = ParseHeaders(headerBytes);

            HeaderBatch = new HeaderBatch(
                headers,
                headerBytes,
                HeaderBatchIndex++);
          }

          if (HeaderBatchOld != null)
          {
            HeaderBatchOld.IsLastBatch = true;
            await Gateway.HeaderBatchDataPipe.InputBuffer.SendAsync(HeaderBatchOld);
          }

          Console.WriteLine("session {0} completes chain syncing", GetHashCode());

          lock(Gateway.LOCK_IsSyncing)
          {
            Gateway.IsSyncingCompleted = true;
          }
        }

        List<Header> ParseHeaders(byte[] headersBytes)
        {
          int startIndex = 0;
          var headers = new List<Header>();

          int headersCount = VarInt.GetInt32(headersBytes, ref startIndex);
          for (int i = 0; i < headersCount; i += 1)
          {
            headers.Add(Header.ParseHeader(headersBytes, ref startIndex, SHA256));
            startIndex += 1; // skip txCount (always a zero-byte)
          }

          return headers;
        }

        async Task SynchronizeUTXOAsync()
        {
          TimeStartChannelInterval = DateTimeOffset.UtcNow;
          BytesDownloaded = 0;
          CountBlocksDownloaded = 0;

          try
          {
            while (Gateway.TryGetDownloadBatch(
              out DownloadBatch,
              CountBlocksDownloadBatch))
            {
              await StartBlockDownloadAsync();
            }

            return;
          }
          catch (Exception ex)
          {
            Gateway.QueueBatchesCanceled.Enqueue(DownloadBatch);

            CountBlocksDownloadBatch = COUNT_BLOCKS_DOWNLOADBATCH_INIT;

            throw ex;
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

        public async Task StartBlockDownloadAsync()
        {
          StopwatchDownload.Restart();

          var cancellationTimeout = new CancellationTokenSource(TIMEOUT_BLOCKDOWNLOAD_MILLISECONDS);
          var cancellationDownloadBlocks = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationTimeout.Token,
            CancellationSession.Token);

          await Channel.RequestBlocksAsync(
            DownloadBatch.Headers.Skip(DownloadBatch.Blocks.Count)
            .Select(h => h.HeaderHash));
                    
          while (DownloadBatch.Blocks.Count < DownloadBatch.Headers.Count)
          {
            byte[] blockBytes = await Channel
              .ReceiveBlockAsync(cancellationDownloadBlocks.Token)
              .ConfigureAwait(false);

            Header header = DownloadBatch.Headers[DownloadBatch.Blocks.Count];

            Block block = BlockParser.ParseBlockHeader(
              blockBytes,
              header,
              header.HeaderHash,
              SHA256);

            DownloadBatch.Blocks.Add(block);
            CountBlocksDownloaded += 1;
            DownloadBatch.BytesDownloaded += blockBytes.Length;
          }

          Gateway.BatcherBuffer.Post(DownloadBatch);

          StopwatchDownload.Stop();

          CalculateNewCountBlocks();

          lock (LOCK_BytesDownloaded)
          {
            BytesDownloaded += DownloadBatch.BytesDownloaded;
          }
        }

        public void CalculateNewCountBlocks()
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


        long BytesDownloadedOld;
        long DownloadRateOld;
        long CountBlocksDownloadedOld;
        public void PrintDownloadMetrics(int timeSpanIntervalMilliSeconds)
        {
          long downloadRate = BytesDownloaded / 
            (int)(DateTimeOffset.UtcNow - TimeStartChannelInterval).TotalMilliseconds;

          Console.WriteLine(
            "{0}({1}),{2}({3})MB,{4}({5})kB/s,{6}({7})",
            GetHashCode(),
            Channel == null ? "not connencted" : Channel.GetIdentification(),
            BytesDownloaded / 1000000,
            BytesDownloadedOld / 1000000,
            downloadRate,
            DownloadRateOld,
            CountBlocksDownloaded,
            CountBlocksDownloadedOld);
        
          BytesDownloadedOld = BytesDownloaded;
          BytesDownloaded = 0;

          DownloadRateOld = downloadRate;
          TimeStartChannelInterval = DateTimeOffset.UtcNow;

          CountBlocksDownloadedOld = CountBlocksDownloaded;
          CountBlocksDownloaded = 0;
        }
      }
    }
  }
}