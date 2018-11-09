using System.Diagnostics;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

using BToken.Networking;

namespace BToken.Chaining
{
  public partial class Blockchain
  {
    partial class BlockchainController
    {
      class SessionBlockDownload
      {
        BlockchainController Controller;

        INetworkChannel Channel;

        const int BatchSize = 50;
        List<ChainBlock> BlocksQueued = new List<ChainBlock>();
        List<ChainBlock> BlocksDownloaded = new List<ChainBlock>();

        BlockArchiver.FileWriter FileWriter;

        int BlocksDispachedCountTotal;


        public SessionBlockDownload(BlockchainController controller)
        {
          Controller = controller;
          //FileWriter = Controller.Blockchain.Archiver.GetWriter();
        }

        public async Task StartAsync(INetworkChannel channel)
        {
          Channel = channel;

          await DownloadBlocksAsync();

          FileWriter.Dispose();
        }
        async Task DownloadBlocksAsync()
        {
          do
          {
            if (BlocksQueued.Count > 0)
            {
              await DownloadBlocksQueuedAsync();

              Debug.WriteLine("Channel '{0}' downloaded '{1}' blocks, Total blocks '{2}'",
                Channel.GetHashCode(), BlocksDownloaded.Count, BlocksDispachedCountTotal += BlocksDownloaded.Count);

              BlocksDownloaded = new List<ChainBlock>();
            }
            
            //BlocksQueued = Controller.Blockchain.GetBlocksUnassignedPayload(BatchSize);

          } while (BlocksQueued.Count > 0);
        }


        async Task DownloadBlocksQueuedAsync()
        {
          var blockPayloads = new List<IBlockPayload>();

          List<UInt256> headerHashesQueued = BlocksQueued.Select(b => GetHeaderHash(b)).ToList();
          await Channel.RequestBlocksAsync(headerHashesQueued);

          while (BlocksQueued.Count > 0)
          {
            CancellationToken cancellationToken = new CancellationTokenSource(TimeSpan.FromSeconds(30)).Token;
            NetworkBlock networkBlock = await GetNetworkBlockAsync(cancellationToken);

            UInt256 networkBlockHeaderHash = GetHeaderHash(networkBlock);
            ChainBlock blockQueued = PopBlockQueued(networkBlock, headerHashesQueued, networkBlockHeaderHash);

            BlockStore payloadStoreID = FileWriter.PeekPayloadID(networkBlock.Payload.Length);
            //Controller.Blockchain.InsertPayload(blockQueued, networkBlock.Payload, payloadStoreID);
            FileWriter.ArchiveBlock(networkBlock);

            BlocksDownloaded.Add(blockQueued);
            BlocksQueued.Remove(blockQueued);
          }
        }

        ChainBlock PopBlockQueued(NetworkBlock networkBlock, List<UInt256> headerHashesQueued, UInt256 networkBlockHash)
        {
          int blockIndex = headerHashesQueued.FindIndex(h => h.IsEqual(networkBlockHash));
          if (blockIndex < 0)
          {
            throw new BlockchainException(BlockCode.ORPHAN);
          }
          headerHashesQueued.RemoveAt(0);

          return BlocksQueued[blockIndex];
        }

        UInt256 GetHeaderHash(ChainBlock chainBlock)
        {
          if (chainBlock == null)
          {
            Console.WriteLine("DispatchBlocks :: BlocksQueued contains null");
          }

          if (chainBlock.BlocksNext.Any())
          {
            return chainBlock.BlocksNext[0].Header.HashPrevious;
          }

          return new UInt256(Hashing.SHA256d(chainBlock.Header.GetBytes()));
        }
        UInt256 GetHeaderHash(NetworkBlock networkBlock)
        {
          return new UInt256(Hashing.SHA256d(networkBlock.Header.GetBytes()));
        }

        async Task<NetworkBlock> GetNetworkBlockAsync(CancellationToken cancellationToken)
        {
          while (true)
          {
            NetworkMessage networkMessage = await Channel.GetNetworkMessageAsync(cancellationToken).ConfigureAwait(false);
            Network.BlockMessage blockMessage = networkMessage as Network.BlockMessage;

            if (blockMessage != null)
            {
              return blockMessage.NetworkBlock;
            }
          }
        }
      }
    }
  }
}
