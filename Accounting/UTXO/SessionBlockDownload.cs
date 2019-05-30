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
        await channel.SendMessageAsync(
          new GetDataMessage(
            HeaderHashes
            .Skip(Blocks.Count)
            .Select(h => new Inventory(InventoryType.MSG_BLOCK, h))
            .ToList()));

        var cancellationGetBlock =
          new CancellationTokenSource(TimeSpan.FromSeconds(SECONDS_TIMEOUT_BLOCKDOWNLOAD));
        
        while (Blocks.Count < HeaderHashes.Length)
        {
          //NetworkMessage networkMessage = 
          //  await channel.ReceiveSessionMessageAsync(cancellationGetBlock.Token);

          //if (networkMessage.Command == "block")
          //{
          //  List<Block> blocks = UTXO.ParseBlocks(this, networkMessage.Payload);

          //  blocks[0].BlockBytes = networkMessage.Payload;
          //  Blocks.Add(blocks[0]);

          //  Console.WriteLine("{0} Downloaded block {1}",
          //    channel.GetIdentification(),
          //    Blocks.Last().HeaderHash.ToHexString());
          //}
        }

        lock (UTXO.LOCK_IndexMerge)
        {
          if (UTXO.IndexMerge != BatchIndex)
          {
            UTXO.QueueBatchsMerge.Add(BatchIndex, this);
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
