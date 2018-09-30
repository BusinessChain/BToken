﻿using System.Diagnostics;

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
      

      async Task DownloadBlocksQueuedAsync()
      {
        var blockPayloads = new List<IBlockPayload>();

        List<UInt256> headerHashesQueued = BlocksQueued.Select(b => GetHeaderHash(b)).ToList();
        await Channel.RequestBlocksAsync(headerHashesQueued);
        
        while (BlocksQueued.Any())
        {
          CancellationToken cancellationToken = new CancellationTokenSource(TimeSpan.FromSeconds(5)).Token;
          NetworkBlock networkBlock = await GetNetworkBlockAsync(cancellationToken);
          
          UInt256 networkBlockHeaderHash = GetHeaderHash(networkBlock);
          ChainBlock blockQueued = PopBlockQueued(networkBlock, headerHashesQueued, networkBlockHeaderHash);

          Validate(blockQueued, networkBlock);
          // wenn die validierung im chainblock erfolgen würde, könnte code zusammengefasst werden.

          blockQueued.BlockStore = Controller.Archiver.ArchiveBlock(networkBlock);

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

      void Validate(ChainBlock chainBlock, NetworkBlock networkBlock)
      {
        IBlockPayload payload = Controller.BlockParser.Parse(networkBlock.Payload);
        UInt256 payloadHash = payload.GetPayloadHash();
        if (!payloadHash.IsEqual(chainBlock.Header.PayloadHash))
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
      
      async Task<NetworkBlock> GetNetworkBlockAsync(CancellationToken cancellationToken)
      {
        while(true)
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
