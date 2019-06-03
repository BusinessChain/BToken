using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using BToken.Networking;


namespace BToken.Accounting
{
  public partial class UTXO
  {
    class SessionBlockDownload : INetworkSession
    {
      UTXO UTXO;
      byte[][] HeaderHashes;

      UTXOBatch Batch;
      
      const int SECONDS_TIMEOUT_BLOCKDOWNLOAD = 30;


      public SessionBlockDownload(
        UTXO uTXO,
        byte[][] headerHashes,
        int batchIndex)
      {
        UTXO = uTXO;
        HeaderHashes = headerHashes;

        Batch = new UTXOBatch(batchIndex);
      }

      public async Task RunAsync(
        INetworkChannel channel, 
        CancellationToken cancellationToken)
      {
        await channel.SendMessageAsync(
          new GetDataMessage(
            HeaderHashes
            .Skip(Batch.Blocks.Count)
            .Select(h => new Inventory(InventoryType.MSG_BLOCK, h))
            .ToList()));

        var cancellationGetBlock =
          new CancellationTokenSource(TimeSpan.FromSeconds(SECONDS_TIMEOUT_BLOCKDOWNLOAD));
        
        while (Batch.Blocks.Count < HeaderHashes.Length)
        {
          NetworkMessage networkMessage =
            await channel.ReceiveSessionMessageAsync(cancellationGetBlock.Token);

          if (networkMessage.Command == "block")
          {
            Batch.Buffer = networkMessage.Payload;
            Batch.BufferIndex = 0;

            Batch.StopwatchParse.Start();
            UTXO.ParseBatch(Batch);
            Batch.StopwatchParse.Stop();

            Console.WriteLine("{0} Downloaded block {1}",
              channel.GetIdentification(),
              Batch.Blocks.Last().HeaderHash.ToHexString());
          }
        }

        await UTXO.MergeBatchAsync(Batch);

        UTXO.ArchiveBatch(Batch);
      }
    }
  }
}
