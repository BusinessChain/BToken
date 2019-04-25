using System.Diagnostics;

using System;
using System.Linq;
using System.IO;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

using BToken.Networking;
using BToken.Chaining;

namespace BToken.Accounting
{
  public partial class UTXO
  {
    class SessionBlockDownload : INetworkSession
    {
      UTXO UTXO;
      HeaderLocation[] HeaderLocations;
      int BatchIndex;
      Block[] BlocksReceived;
      int IndexBlockReceived;
      StreamWriter BuildWriter;

      public INetworkChannel Channel { get; private set; }

      const int SECONDS_TIMEOUT_BLOCKDOWNLOAD = 20;


      public SessionBlockDownload(
        UTXO uTXO, 
        HeaderLocation[] headerLocations, 
        int batchIndex,
        StreamWriter buildWriter)
      {
        UTXO = uTXO;
        HeaderLocations = headerLocations;
        BatchIndex = batchIndex;
        BlocksReceived = new Block[headerLocations.Length];
        IndexBlockReceived = 0;
        BuildWriter = buildWriter;
      }

      public async Task RunAsync(INetworkChannel channel, CancellationToken cancellationToken)
      {
        Channel = channel;
        
        List<Inventory> inventories = HeaderLocations
          .Skip(IndexBlockReceived)
          .Select(h => new Inventory(InventoryType.MSG_BLOCK, h.Hash))
          .ToList();

        await Channel.SendMessageAsync(new GetDataMessage(inventories));

        var CancellationGetBlock =
          new CancellationTokenSource(TimeSpan.FromSeconds(SECONDS_TIMEOUT_BLOCKDOWNLOAD));

        while (IndexBlockReceived < HeaderLocations.Length)
        {
          NetworkMessage networkMessage = await Channel.ReceiveSessionMessageAsync(CancellationGetBlock.Token);

          if (networkMessage.Command == "block")
          {
            byte[] blockBytes = networkMessage.Payload;

            int startIndex = 0;
            NetworkHeader header = NetworkHeader.ParseHeader(
              blockBytes,
              out int tXCount,
              ref startIndex);

            UInt256 headerHash = header.ComputeHash(out byte[] headerHashBytes);
            UInt256 hash = HeaderLocations[IndexBlockReceived].Hash;
            if (!hash.Equals(headerHash))
            {
              throw new UTXOException("Unexpected header hash.");
            }

            TX[] tXs = UTXO.ParseBlock(blockBytes, ref startIndex, tXCount);
            byte[] merkleRootHash = UTXO.ComputeMerkleRootHash(tXs, out byte[][] tXHashes);
            if (!EqualityComparerByteArray.IsEqual(header.MerkleRoot, merkleRootHash))
            {
              throw new UTXOException("Payload corrupted.");
            }

            BlocksReceived[IndexBlockReceived] = new Block(
              headerHashBytes,
              tXs,
              tXHashes,
              blockBytes,
              HeaderLocations[IndexBlockReceived].Height);

            //Console.WriteLine("'{0}' Downloaded block '{1}', height {2}", 
            //  Channel.GetIdentification(),
            //  hash,
            //  HeaderLocations[IndexBlockReceived].Height);

            IndexBlockReceived++;
          }
        }
        
        lock (UTXO.MergeLOCK)
        {
          if (UTXO.MergeBatchIndex != BatchIndex)
          {
            UTXO.QueueMergeBlocks.Add(BatchIndex, BlocksReceived);
            return;
          }
        }

        long countBlockBytesDownloadedBatch = 0;
        while (true)
        {
          for (int i = 0; i < BlocksReceived.Length; i++)
          {
            UTXO.Merge(
              BlocksReceived[i].TXs,
              BlocksReceived[i].TXHashes,
              BlocksReceived[i].HeaderHashBytes);

            UTXO.BlocksPartitioned.Add(BlocksReceived[i]);
            UTXO.CountTXsPartitioned += BlocksReceived[i].TXs.Length;

            if (UTXO.CountTXsPartitioned > MAX_COUNT_TXS_IN_PARTITION)
            {
              Task archiveBlocksTask = BlockArchiver.ArchiveBlocksAsync(
                UTXO.BlocksPartitioned,
                UTXO.FilePartitionIndex++);

              UTXO.BlocksPartitioned = new List<Block>();
              UTXO.CountTXsPartitioned = 0;
            }

            countBlockBytesDownloadedBatch += BlocksReceived[i].BlockBytes.Length;
          }

          UTXO.CountBlockBytesDownloadedTotal += countBlockBytesDownloadedBatch;

          string metricsCSV = string.Format("{0},{1},{2},{3},{4}",
            UTXO.MergeBatchIndex,
            DateTimeOffset.UtcNow.ToUnixTimeSeconds() - UTCTimeStartup,
            BlocksReceived.Last().Height,
            UTXO.CountBlockBytesDownloadedTotal,
            UTXO.Cache.GetMetricsCSV());

          Console.WriteLine(metricsCSV);
          BuildWriter.WriteLine(metricsCSV);

          lock (UTXO.MergeLOCK)
          {
            UTXO.MergeBatchIndex++;

            if (UTXO.QueueMergeBlocks.TryGetValue(UTXO.MergeBatchIndex, out BlocksReceived))
            {
              UTXO.QueueMergeBlocks.Remove(UTXO.MergeBatchIndex);
            }
            else
            {
              return;
            }
          }
        }
      }

      public string GetSessionID()
      {
        return BatchIndex.ToString();
      }
    }
  }
}
