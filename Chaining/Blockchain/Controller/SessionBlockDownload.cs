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
  partial class BlockchainController
  {
    class SessionBlockDownload : BlockchainSession
    {
      BlockchainController Controller;

      BlockPayloadLocator BlockLocator;
      List<ChainBlock> BlocksQueued = new List<ChainBlock>();
      List<ChainBlock> BlocksDownloaded = new List<ChainBlock>();
      
      int BlocksDispachedCountTotal;
      

      public SessionBlockDownload(BlockchainController controller, BlockPayloadLocator blockLocator)
      {
        Controller = controller;
        BlockLocator = blockLocator;
      }

      public override async Task StartAsync(BlockchainChannel channel)
      {
        Channel = channel;

        await DownloadBlocksAsync();
      }
      async Task DownloadBlocksAsync()
      {
        do
        {
          if (BlocksQueued.Any())
          {
            await DownloadBlocksQueuedAsync();

            Debug.WriteLine("Channel '{0}' downloaded '{1}' blocks, Total blocks '{2}'",
              Channel.GetHashCode(), BlocksDownloaded.Count, BlocksDispachedCountTotal += BlocksDownloaded.Count);
            
            BlockLocator.RemoveDownloaded(BlocksDownloaded);
            BlocksDownloaded = new List<ChainBlock>();
          }

          BlocksQueued = BlockLocator.DispatchBlocks();

        } while (BlocksQueued.Any());
      }
      

      async Task<List<IBlockPayload>> DownloadBlocksQueuedAsync()
      {
        var blockPayloads = new List<IBlockPayload>();

        List<UInt256> headerHashesQueued = BlocksQueued.Select(b => GetHeaderHash(b)).ToList();

        await Channel.RequestBlocksAsync(headerHashesQueued);


        while (BlocksQueued.Any())
        {
          CancellationToken cancellationToken = new CancellationTokenSource(TimeSpan.FromSeconds(10)).Token;
          NetworkBlock networkBlock = await GetBlockMessageAsync(cancellationToken);
          
          UInt256 networkBlockHeaderHash = GetHeaderHash(networkBlock);
          ChainBlock blockQueued = PopBlockQueued(networkBlock, headerHashesQueued, networkBlockHeaderHash);

          Validate(blockQueued, networkBlock);

          blockQueued.BlockStore = Controller.Archiver.ArchiveBlock(networkBlock);

          BlocksDownloaded.Add(blockQueued);
          BlocksQueued.Remove(blockQueued);

        }

        return blockPayloads;
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

      void Validate(ChainBlock blockQueued, NetworkBlock networkBlock)
      {
        IBlockPayload payload = Controller.BlockParser.Parse(networkBlock.Payload);
        UInt256 payloadHash = payload.GetPayloadHash();
        if (!payloadHash.IsEqual(blockQueued.Header.PayloadHash))
        {
          throw new BlockchainException(BlockCode.INVALID);
        }
      }

      UInt256 GetHeaderHash(ChainBlock chainBlock)
      {
        if(chainBlock.BlocksNext.Any())
        {
          return chainBlock.BlocksNext[0].Header.HashPrevious;
        }

        return new UInt256(Hashing.SHA256d(chainBlock.Header.getBytes()));
      }
      UInt256 GetHeaderHash(NetworkBlock networkBlock)
      {
        return new UInt256(Hashing.SHA256d(networkBlock.Header.getBytes()));
      }
      
      async Task<NetworkBlock> GetBlockMessageAsync(CancellationToken cancellationToken)
      {
        Network.BlockMessage blockMessage = await Channel.GetNetworkMessageAsync(cancellationToken).ConfigureAwait(false) as Network.BlockMessage;

        if(blockMessage != null)
        {
          return blockMessage.NetworkBlock;
        }

        return await GetBlockMessageAsync(cancellationToken).ConfigureAwait(false);
      }
    }
  }
}
