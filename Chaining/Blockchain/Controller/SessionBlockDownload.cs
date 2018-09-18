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
      List<ChainBlock> BlocksDispatched = new List<ChainBlock>();


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

            BlockLocator.RemoveDispatched(BlocksDispatched);
            BlocksDispatched = new List<ChainBlock>();
          }

          BlocksQueued = BlockLocator.DispatchBlocks();

        } while (BlocksQueued.Any());
      }
      
      async Task DownloadBlocksQueuedAsync()
      {
        List<UInt256> headerHashesQueued = BlocksQueued.Select(b => GetHeaderHash(b)).ToList();
        await Channel.RequestBlocksAsync(headerHashesQueued).ConfigureAwait(false);

        while (BlocksQueued.Any())
        {
          //CancellationToken cancellationToken = new CancellationTokenSource(TimeSpan.FromSeconds(2)).Token;
          NetworkBlock networkBlock = await GetBlockMessageAsync().ConfigureAwait(false);

          UInt256 networkBlockHeaderHash = GetHeaderHash(networkBlock);
          ChainBlock blockQueued = PopBlockQueued(networkBlock, headerHashesQueued, networkBlockHeaderHash);

          Validate(blockQueued, networkBlock, out IBlockPayload blockPayload);

          blockQueued.BlockPayload = blockPayload;
          blockPayload.StoreToDisk(blockQueued.Header, networkBlockHeaderHash.ToString());

          BlocksDispatched.Add(blockQueued);
          BlocksQueued.Remove(blockQueued);

          Debug.WriteLine("Channel '{0}' downloaded block '{1}', Total blocks '{2}'", 
            Channel.GetHashCode(), networkBlockHeaderHash.ToString(), ++BlocksDispachedCountTotal);
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

      void Validate(ChainBlock blockQueued, NetworkBlock networkBlock, out IBlockPayload blockPayload)
      {
        blockPayload = Controller.BlockParser.Parse(networkBlock.Payload);
        UInt256 payloadHash = blockPayload.GetPayloadHash();
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
      
      async Task<NetworkBlock> GetBlockMessageAsync()
      {
        Network.BlockMessage blockMessage = await Channel.GetNetworkMessageAsync(default(CancellationToken)).ConfigureAwait(false) as Network.BlockMessage;

        if(blockMessage != null)
        {
          return blockMessage.NetworkBlock;
        }

        return await GetBlockMessageAsync().ConfigureAwait(false);
      }
    }
  }
}
