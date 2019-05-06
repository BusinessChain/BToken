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
    class SessionBlockDownload : BatchBlockLoad, INetworkSession
    {
      UTXO UTXO;
      HeaderLocation[] HeaderLocations;

      public INetworkChannel Channel { get; private set; }

      const int SECONDS_TIMEOUT_BLOCKDOWNLOAD = 20;


      public SessionBlockDownload(
        UTXO uTXO,
        HeaderLocation[] headerLocations,
        int batchIndex)
        : base(batchIndex)
      {
        UTXO = uTXO;
        HeaderLocations = headerLocations;
      }

      public async Task RunAsync(INetworkChannel channel, CancellationToken cancellationToken)
      {
        Channel = channel;

        List<Inventory> inventories = HeaderLocations
          .Skip(Blocks.Count)
          .Select(h => new Inventory(InventoryType.MSG_BLOCK, h.Hash))
          .ToList();

        await Channel.SendMessageAsync(new GetDataMessage(inventories));

        var CancellationGetBlock =
          new CancellationTokenSource(TimeSpan.FromSeconds(SECONDS_TIMEOUT_BLOCKDOWNLOAD));
        
        while (Blocks.Count < HeaderLocations.Length)
        {
          NetworkMessage networkMessage = await Channel.ReceiveSessionMessageAsync(CancellationGetBlock.Token);

          if (networkMessage.Command == "block")
          {
            Blocks.AddRange(
              UTXO.ParseBlocks(
                this,
                networkMessage.Payload));

            Console.WriteLine("'{0}' Downloaded block '{1}', height {2}",
              Channel.GetIdentification(),
              Blocks.Last().HeaderHash.ToHexString(),
              HeaderLocations[Blocks.Count].Height);
          }
        }
        
        UTXO.BlocksPartitioned.AddRange(Blocks);
        UTXO.CountTXsPartitioned += Blocks.Sum(b => b.TXs.Length);

        if (UTXO.CountTXsPartitioned > MAX_COUNT_TXS_IN_PARTITION)
        {
          Task archiveBlocksTask = BlockArchiver.ArchiveBlocksAsync(
            UTXO.BlocksPartitioned,
            UTXO.FilePartitionIndex);

          UTXO.FilePartitionIndex += 1;

          UTXO.BlocksPartitioned = new List<Block>();
          UTXO.CountTXsPartitioned = 0;
        }


        lock (UTXO.MergeLOCK)
        {
          if (UTXO.IndexBatchMerge != BatchIndex)
          {
            UTXO.QueueBlocksMerge.Add(BatchIndex, this);
            return;
          }
        }

        UTXO.Merge(this);
      }

      public List<Block> GetBlocks()
      {
        return Blocks;
      }
      public Headerchain.ChainHeader GetChainHeader()
      {
        return ChainHeader;
      }

      public string GetSessionID()
      {
        return BatchIndex.ToString();
      }
    }
  }
}
