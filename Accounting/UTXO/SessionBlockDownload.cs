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
      class SessionBlockDownload : INetworkSession
      {
        INetworkChannel Channel;

        const int SECONDS_TIMEOUT_BLOCKDOWNLOAD = 20;

        UTXOBuilder Builder;
        UTXOParser Parser;
        SHA256 SHA256;

        UTXODownloadBatch DownloadBatch;

        long BytesDownloaded;


        public SessionBlockDownload(UTXOBuilder builder)
        {
          Builder = builder;
          Parser = new UTXOParser(Builder.UTXO);
          SHA256 = SHA256.Create();
        }
        
        async Task DownloadBlocksAsync()
        {
          await Channel.SendMessageAsync(
            new GetDataMessage(
              DownloadBatch.Headers
              .Select(h => new Inventory(InventoryType.MSG_BLOCK, h.GetHeaderHash(SHA256)))
              .ToList()));

          var cancellationGetData = new CancellationTokenSource(
            TimeSpan.FromSeconds(SECONDS_TIMEOUT_BLOCKDOWNLOAD));

          while (DownloadBatch.Blocks.Count < COUNT_BLOCKS_DOWNLOAD_BATCH)
          {
            NetworkMessage networkMessage = await Channel
              .ReceiveSessionMessageAsync(cancellationGetData.Token)
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

          BytesDownloaded += DownloadBatch.BytesDownloaded;

          Builder.PostDownloadBatch(DownloadBatch);
        }

        public async Task RunAsync(INetworkChannel channel)
        {
          Channel = channel;

          try
          {
            if (DownloadBatch != null)
            {
              DownloadBatch.Blocks.Clear();

              await DownloadBlocksAsync();
            }

            while (true)
            {
              if (!Builder.QueueDownloadBatchesCanceled.TryDequeue(out DownloadBatch))
              {
                try
                {
                  DownloadBatch = await Builder.DownloaderBuffer
                    .ReceiveAsync(Builder.CancellationBuilder.Token).ConfigureAwait(false);
                }
                catch(TaskCanceledException)
                {
                  return;
                }
              }

              await DownloadBlocksAsync();
            }
          }
          catch (TaskCanceledException ex)
          {
            lock (Builder.LOCK_CountDownloadTasksRunning)
            {
              Console.WriteLine("Count download tasks running: " 
                + Builder.CountDownloadTasksRunning);

              if (Builder.CountDownloadTasksRunning > 1)
              {
                DownloadBatch.Blocks.Clear();

                Builder.QueueDownloadBatchesCanceled.Enqueue(DownloadBatch);
                Builder.CountDownloadTasksRunning -= 1;
                return;
              }
            }

            throw ex;
          }
        }
      }
    }
  }
}