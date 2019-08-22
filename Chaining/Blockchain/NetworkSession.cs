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
    partial class BlockchainNetworkGateway
    {
      class NetworkSession
      {
        public INetworkChannel Channel;

        const int COUNT_BLOCKS_DOWNLOADBATCH_INIT = 1;
        const int INTERVAL_DOWNLOAD_CONTROLLER_MILLISECONDS = 30000;
        const int TIMEOUT_BLOCKDOWNLOAD_MILLISECONDS = 20000;

        BlockchainNetworkGateway Gateway;
        BlockParser Parser;
        SHA256 SHA256;

        public SESSION_STATE State = SESSION_STATE.IDLE;
        readonly object LOCK_State = new object();
        bool IsSyncingChain;

        Stopwatch StopwatchDownload = new Stopwatch();
        public int CountBlocksDownloadBatch = COUNT_BLOCKS_DOWNLOADBATCH_INIT;
        public DownloadBatch DownloadBatch;
        public CancellationTokenSource CancellationSession;

        readonly object LOCK_BytesDownloaded = new object();
        long BytesDownloaded;
        int CountBlocksDownloaded;

        public DateTimeOffset TimeStartChannelInterval;

        public NetworkSession(BlockchainNetworkGateway gateway, BlockParser parser)
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

              await Gateway.ChainSyncingCompleted.Task;

              await SynchronizeUTXOAsync();
              await RunListenerAsync();

              Console.WriteLine("session {0} ends synchronizing, starts listening.", GetHashCode());
            }
            catch (Exception ex)
            {
              Console.WriteLine("Exception: '{0}' in session {1} with channel {2}", 
                ex.Message,
                GetHashCode(),
                Channel == null ? "" : Channel.GetIdentification());

              if(IsSyncingChain)
              {
                lock (Gateway.LOCK_IsSyncingChain)
                {
                  IsSyncingChain = false;
                  Gateway.IsSyncingChain = false;
                }
              }
            }
          }
        }

        async Task SynchronizeChainAsync()
        {
          byte[] headerBytes = await Channel.GetHeadersAsync(
            Gateway.Blockchain.GetChainLocator());

          List<Header> headers = ParseHeaders(headerBytes);
          int countHeaders = headers.Count;

          lock (Gateway.LOCK_IsSyncingChain)
          {
            if (Gateway.IsSyncingChain)
            {
              return;
            }
            
            IsSyncingChain = true;
            Gateway.IsSyncingChain = true;
          }

          Console.WriteLine("session {0} enters header syncing", GetHashCode());

          List<byte[]> headerBatchesBytes = new List<byte[]>();
          headerBatchesBytes.Add(headerBytes);

          while (headers.Any())
          {
            headerBytes = await Channel.GetHeadersAsync(
              new List<byte[]> { headers.Last().HeaderHash });

            headers = ParseHeaders(headerBytes);
            headerBatchesBytes.Add(headerBytes);

            countHeaders += headers.Count;

            Console.WriteLine("downloaded {0} headers", countHeaders);
          }

          Console.WriteLine("session {0} completes syncing", GetHashCode());
          Gateway.ChainSyncingCompleted.SetResult(null);
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

          await Channel.SendMessageAsync(
            new GetDataMessage(
              DownloadBatch.Headers.Skip(DownloadBatch.Blocks.Count)
              .Select(h => new Inventory(InventoryType.MSG_BLOCK, h.HeaderHash))
              .ToList()));

          while (DownloadBatch.Blocks.Count < DownloadBatch.Headers.Count)
          {
            NetworkMessage networkMessage = await Channel
              .ReceiveSessionMessageAsync(cancellationDownloadBlocks.Token)
              .ConfigureAwait(false);

            if (networkMessage.Command != "block")
            {
              continue;
            }

            Header header = DownloadBatch.Headers[DownloadBatch.Blocks.Count];

            Block block = BlockParser.ParseBlockHeader(
              networkMessage.Payload,
              header,
              header.HeaderHash,
              SHA256);

            DownloadBatch.Blocks.Add(block);
            CountBlocksDownloaded += 1;
            DownloadBatch.BytesDownloaded += networkMessage.Payload.Length;
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