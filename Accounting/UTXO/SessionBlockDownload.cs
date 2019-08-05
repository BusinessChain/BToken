using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Threading.Tasks.Dataflow;
using System.Security.Cryptography;

using BToken.Chaining;
using BToken.Networking;


namespace BToken.Accounting
{
  public partial class UTXO
  {
    partial class UTXONetworkLoader
    {
      class SessionBlockDownload : INetworkSession
      {
        INetworkChannel Channel;

        const int TIMEOUT_BLOCKDOWNLOAD_MILLISECONDS = 30000;

        UTXONetworkLoader Loader;
        UTXOParser Parser;
        SHA256 SHA256;

        public UTXODownloadBatch DownloadBatch;
        public CancellationTokenSource CancellationSession;

        readonly object LOCK_BytesDownloaded = new object();
        long BytesDownloaded;

        long UTCTimeStartSession;


        public SessionBlockDownload(UTXONetworkLoader loader, UTXOParser parser)
        {
          Loader = loader;
          Parser = parser;
          SHA256 = SHA256.Create();

          CancellationSession = CancellationTokenSource.CreateLinkedTokenSource(Loader.CancellationLoader.Token);
          UTCTimeStartSession = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        }

        public async Task RunAsync(INetworkChannel channel)
        {
          Channel = channel;

          try
          {
            if (DownloadBatch != null)
            {
              await DownloadBlocksAsync();

              Loader.PostDownload(DownloadBatch);
            }

            while (true)
            {
              if (!Loader.QueueDownloadBatchesCanceled.TryDequeue(out DownloadBatch))
              {
                DownloadBatch = await Loader.DownloaderBuffer
                  .ReceiveAsync(CancellationSession.Token).ConfigureAwait(false);
              }

              await DownloadBlocksAsync();

              Loader.PostDownload(DownloadBatch);
            }
          }
          catch (TaskCanceledException)
          {
            if (DownloadBatch != null)
            {
              Loader.QueueDownloadBatchesCanceled.Enqueue(DownloadBatch);
            }

            lock (Loader.LOCK_DownloadSessions)
            {
              Loader.DownloadSessions.Remove(this);
            }

            Console.WriteLine("Session {0} cancels", GetHashCode());

            return;
          }
        }
        async Task DownloadBlocksAsync()
        {
          var cancellationTimeout = new CancellationTokenSource(TIMEOUT_BLOCKDOWNLOAD_MILLISECONDS);
          var cancellationDownloadBlocks = CancellationTokenSource.CreateLinkedTokenSource(
          cancellationTimeout.Token,
          CancellationSession.Token);

          await Channel.SendMessageAsync(
              new GetDataMessage(
                DownloadBatch.Headers
                .Select(h => new Inventory(InventoryType.MSG_BLOCK, h.GetHeaderHash(SHA256)))
                .ToList()));

          DownloadBatch.Blocks.Clear();
          DownloadBatch.BytesDownloaded = 0;
          while (DownloadBatch.Blocks.Count < DownloadBatch.Headers.Count)
          {
            NetworkMessage networkMessage = await Channel
              .ReceiveSessionMessageAsync(cancellationDownloadBlocks.Token)
              .ConfigureAwait(false);

            if (networkMessage.Command != "block")
            {
              continue;
            }

            Headerchain.ChainHeader header = DownloadBatch.Headers[DownloadBatch.Blocks.Count];

            Block block = UTXOParser.ParseBlockHeader(
              networkMessage.Payload,
              header,
              header.GetHeaderHash(SHA256),
              SHA256);

            DownloadBatch.Blocks.Add(block);
            DownloadBatch.BytesDownloaded += networkMessage.Payload.Length;
          }

          lock (LOCK_BytesDownloaded)
          {
            BytesDownloaded += DownloadBatch.BytesDownloaded;
          }
        }

        public void ResetStats()
        {
          lock (LOCK_BytesDownloaded)
          {
            BytesDownloaded = 0;
          }

          UTCTimeStartSession = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        }
        public long GetDownloadRatekiloBytePerSecond()
        {
          long bytesDownloaded = GetBytesDownloaded();
          long secondsSinceStartSession = DateTimeOffset.UtcNow.ToUnixTimeSeconds() - UTCTimeStartSession;

          return bytesDownloaded / secondsSinceStartSession / 1000;
        }
        public long GetBytesDownloaded()
        {
          long bytesDownloaded;

          lock (LOCK_BytesDownloaded)
          {
            bytesDownloaded = BytesDownloaded;
          }

          return bytesDownloaded;
        }
      }
    }
  }
}