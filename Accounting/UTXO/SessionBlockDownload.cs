using System;
using System.Linq;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using System.Security.Cryptography;

using BToken.Networking;
using BToken.Chaining;


namespace BToken.Accounting
{
  public partial class UTXO
  {
    class SessionBlockDownload : INetworkSession
    {
      const int SECONDS_TIMEOUT_BLOCKDOWNLOAD = 30;
      const int COUNT_BLOCKS_DOWNLOAD = 10;

      UTXO UTXO;
      public List<Block> Blocks = new List<Block>(COUNT_BLOCKS_DOWNLOAD);
      SHA256 SHA256;


      public SessionBlockDownload(UTXO uTXO)
      {
        UTXO = uTXO;
        SHA256 = SHA256.Create();
      }

      public async Task RunAsync(
        INetworkChannel channel, 
        CancellationToken cancellationToken)
      {
        Blocks.Clear();

        while (UTXO.TryGetHeaderHashes(
          out byte[][] headerHashes,
          out int downloadIndex,
          COUNT_BLOCKS_DOWNLOAD,
          SHA256))
        {
          await channel.SendMessageAsync(
            new GetDataMessage(
              headerHashes
              .Select(h => new Inventory(InventoryType.MSG_BLOCK, h))
              .ToList()));

          var cancellationGetBlock =
            new CancellationTokenSource(TimeSpan.FromSeconds(SECONDS_TIMEOUT_BLOCKDOWNLOAD));
          
          while (Blocks.Count < headerHashes.Length)
          {
            NetworkMessage networkMessage =
              await channel.ReceiveSessionMessageAsync(cancellationGetBlock.Token);

            if (networkMessage.Command == "block")
            {
              Block block = UTXO.Parser.ParseBlock(
                networkMessage.Payload,
                0,
                headerHashes[Blocks.Count],
                SHA256);

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
            downloadIndex);

          UTXO.MergeBlocks(Blocks, downloadIndex);
        }
      }
    }
  }
}
