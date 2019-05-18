using System.Diagnostics;
using System;
using System.Linq;
using System.IO;
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
    class SessionBlockDownload : UTXOBatch, INetworkSession
    {
      UTXO UTXO;
      byte[][] HeaderHashes;

      public INetworkChannel Channel { get; private set; }

      const int SECONDS_TIMEOUT_BLOCKDOWNLOAD = 20;


      public SessionBlockDownload(
        UTXO uTXO,
        byte[][] headerHashes,
        int batchIndex)
        : base(batchIndex)
      {
        UTXO = uTXO;
        HeaderHashes = headerHashes;
      }

      public async Task RunAsync(INetworkChannel channel, CancellationToken cancellationToken)
      {
        Channel = channel;

        List<Inventory> inventories = HeaderHashes
          .Skip(Blocks.Count)
          .Select(h => new Inventory(InventoryType.MSG_BLOCK, h))
          .ToList();

        await Channel.SendMessageAsync(new GetDataMessage(inventories));

        var CancellationGetBlock =
          new CancellationTokenSource(TimeSpan.FromSeconds(SECONDS_TIMEOUT_BLOCKDOWNLOAD));
        
        while (Blocks.Count < HeaderHashes.Length)
        {
          NetworkMessage networkMessage = await Channel.ReceiveSessionMessageAsync(CancellationGetBlock.Token);

          if (networkMessage.Command == "block")
          {
            //List<Block> blocks = UTXO.ParseBlocks(this, networkMessage.Payload);

            //blocks[0].BlockBytes = networkMessage.Payload;
            //Blocks.Add(blocks[0]);

            //Console.WriteLine("{0} Downloaded block {1}",
            //  Channel.GetIdentification(),
            //  Blocks.Last().HeaderHash.ToHexString());
          }
        }

        lock (UTXO.MergeLOCK)
        {
          if (UTXO.IndexBatchMerge != Index)
          {
            UTXO.QueueBatchsMerge.Add(Index, this);
            return;
          }
        }

        //UTXO.ArchiveBatch(batch.Index, batch.Blocks);
        //Task cacheArchivingTask = UTXO.Cache.ArchiveAsync();

        //UTXO.Merge(
        //  this,
        //  flagArchive: true);
      }
    }
  }
}
