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
        public List<Block> Blocks = new List<Block>(COUNT_BLOCKS_DOWNLOAD_BATCH);
        SHA256 SHA256;


        public SessionBlockDownload(UTXOBuilder builder)
        {
          Builder = builder;
          Parser = new UTXOParser(Builder.UTXO);
          SHA256 = SHA256.Create();
        }

        public async Task RunAsync(INetworkChannel channel)
        {
          Blocks.Clear();

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

            while (Blocks.Count < downloadBatch.HeaderHashes.Count)
            {
              NetworkMessage networkMessage =
                await channel.ReceiveSessionMessageAsync(cancellationGetData.Token);

              if (networkMessage.Command == "block")
              {
                Parser.ParseHeader(out int indexMerkleRoot, out byte[] headerHash);

                Block block = Parser.ParseBlock(headerHash, indexMerkleRoot);

                Blocks.Add(block);

                //Console.WriteLine("{0}, {1} Downloaded block {2}",
                //  DateTime.Now,
                //  channel.GetIdentification(),
                //  Batch.Blocks.Last().HeaderHash.ToHexString());
              }
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
