using System.Diagnostics;

using System;
using System.Linq;
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

      public INetworkChannel Channel { get; private set; }

      const int SECONDS_TIMEOUT_BLOCKDOWNLOAD = 20;


      public SessionBlockDownload(UTXO uTXO, HeaderLocation[] headerLocations, int batchIndex)
      {
        UTXO = uTXO;
        HeaderLocations = headerLocations;
        BatchIndex = batchIndex;
        BlocksReceived = new Block[headerLocations.Length];
        IndexBlockReceived = 0;
      }

      public async Task RunAsync(INetworkChannel channel, CancellationToken cancellationToken)
      {
        Channel = channel;

        try
        {
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
              Console.WriteLine("Postpone merge of batch {0}, merge index: {1}", BatchIndex, UTXO.MergeBatchIndex);
              UTXO.QueueMergeBlocks.Add(BatchIndex, BlocksReceived);
              return;
            }
          }

          Console.WriteLine("Merge batch {0}, block height {1}", BatchIndex, BlocksReceived[0].Height);

          while (true)
          {
            for(int i = 0; i < BlocksReceived.Length; i++)
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
            }

            Console.WriteLine("Successfully merged batch {0}", UTXO.MergeBatchIndex);

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
        catch (TaskCanceledException ex)
        {
          Console.WriteLine("Canceled download of block batch {0} from peer {1} due to timeout {2} seconds",
            BatchIndex,
            Channel.GetIdentification(),
            SECONDS_TIMEOUT_BLOCKDOWNLOAD);

          throw ex;
        }
        catch(Exception ex)
        {
          Console.WriteLine(ex.Message);
        }
      }

    }
  }
}
