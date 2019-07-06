using System;
using System.Linq;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using System.Threading.Tasks.Dataflow;
using System.Security.Cryptography;

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


        public SessionBlockDownload(UTXOBuilder builder)
        {
          Builder = builder;
          Parser = new UTXOParser(Builder.UTXO);
          SHA256 = SHA256.Create();
        }
        
        public async Task RunAsync(INetworkChannel channel)
        {
          Channel = channel;

          if (DownloadBatch != null)
          {
            DownloadBatch.Blocks.Clear();

            await DownloadBlocksAsync();

            Builder.BatcherBuffer.Post(DownloadBatch);
          }

          while (true)
          {
            try
            {
              DownloadBatch = await Builder.DownloaderBuffer
                .ReceiveAsync(Builder.CancellationBuilder.Token).ConfigureAwait(false);
            }
            catch (TaskCanceledException)
            {
              return;
            }

            await DownloadBlocksAsync();

            Builder.BatcherBuffer.Post(DownloadBatch);
          }
        }

        async Task DownloadBlocksAsync()
        {
          await Channel.SendMessageAsync(
            new GetDataMessage(
              DownloadBatch.HeaderHashes
              .Select(h => new Inventory(InventoryType.MSG_BLOCK, h))
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

            Block block = UTXOParser.ParseBlockHeader(
              networkMessage.Payload,
              DownloadBatch.HeaderHashes[DownloadBatch.Blocks.Count],
              SHA256);

            DownloadBatch.Blocks.Add(block);
          }
        }
      }
    }
  }
}