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
    partial class UTXOBuilder
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


          public SessionBlockDownload(UTXONetworkLoader loader, UTXOParser parser)
          {
            Loader = loader;
            Parser = parser;
            SHA256 = SHA256.Create();

            CancellationSession = CancellationTokenSource.CreateLinkedTokenSource(Loader.CancellationToken);
          }
                    
          public async Task RunAsync(INetworkChannel channel)
          {
            Channel = channel;
            
            try
            {
              if (DownloadBatch != null)
              {
                await DownloadBlocksAsync();

                Loader.PostDownloadToBatcher(DownloadBatch);
              }

              while (true)
              {
                if (!Loader.QueueDownloadBatchesCanceled.TryDequeue(out DownloadBatch))
                {
                  DownloadBatch = await Loader.DownloaderBuffer
                    .ReceiveAsync(CancellationSession.Token).ConfigureAwait(false);
                }
                
                await DownloadBlocksAsync();

                Loader.PostDownloadToBatcher(DownloadBatch);
              }
            }
            catch (TaskCanceledException)
            {
              Loader.CancelSession(this);
              return;
            }
          }
          async Task DownloadBlocksAsync()
          {
            var cancellationDownloadBlocks = CancellationTokenSource.CreateLinkedTokenSource(
              new CancellationTokenSource(TIMEOUT_BLOCKDOWNLOAD_MILLISECONDS).Token,
              CancellationSession.Token);

            await Channel.SendMessageAsync(
              new GetDataMessage(
                DownloadBatch.Headers
                .Select(h => new Inventory(InventoryType.MSG_BLOCK, h.GetHeaderHash(SHA256)))
                .ToList()));

            DownloadBatch.Blocks.Clear();
            while (DownloadBatch.Blocks.Count < COUNT_BLOCKS_DOWNLOAD_BATCH)
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

            lock(LOCK_BytesDownloaded)
            {
              BytesDownloaded += DownloadBatch.BytesDownloaded;
            }
          }

          public long GetBytesDownloaded()
          {
            long bytesDownloaded;

            lock(LOCK_BytesDownloaded)
            {
              bytesDownloaded = BytesDownloaded;
            }

            return bytesDownloaded;
          }
        }
      }
    }
  }
}