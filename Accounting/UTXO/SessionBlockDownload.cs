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
        const int SECONDS_TIMEOUT_BLOCKDOWNLOAD = 30;

        UTXOBuilder Builder;
        UTXOParser Parser;
        SHA256 SHA256;


        public SessionBlockDownload(UTXOBuilder builder)
        {
          Builder = builder;
          Parser = new UTXOParser(Builder.UTXO);
          SHA256 = SHA256.Create();
        }

        public async Task RunAsync(INetworkChannel channel)
        {
          UTXODownloadBatch downloadBatch;

          while (true)
          {
            try
            {
              downloadBatch = await Builder.DownloaderBuffer
                .ReceiveAsync(Builder.CancellationBuilder.Token).ConfigureAwait(false);
            }
            catch(TaskCanceledException)
            {
              return;
            }
                       
            await channel.SendMessageAsync(
              new GetDataMessage(
                downloadBatch.HeaderHashes
                .Select(h => new Inventory(InventoryType.MSG_BLOCK, h))
                .ToList()));

            var cancellationGetData = new CancellationTokenSource(
              TimeSpan.FromSeconds(SECONDS_TIMEOUT_BLOCKDOWNLOAD));
            
            while (downloadBatch.Blocks.Count < COUNT_BLOCKS_DOWNLOAD_BATCH)
            {
              NetworkMessage networkMessage = await channel
                .ReceiveSessionMessageAsync(cancellationGetData.Token)
                .ConfigureAwait(false);

              if (networkMessage.Command != "block")
              {
                continue;
              }

              Block block = new Block(networkMessage.Payload, 0);

              UTXOParser.ParseHeader(
                block,
                SHA256);

              UTXOParser.ValidateHeaderHash(
                block.HeaderHash, 
                downloadBatch.HeaderHashes[downloadBatch.Blocks.Count]);

              downloadBatch.Blocks.Add(block);
            }

            Console.WriteLine("{0}, {1} Download index {2}",
              DateTime.Now,
              channel.GetIdentification(),
              downloadBatch.BatchIndex);

            Builder.BatcherBuffer.Post(downloadBatch);
          }
        }
      }
    }
  }
}
