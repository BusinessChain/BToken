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
      Blockchain Blockchain;

      BlockPayloadLocator BlockHeaderLocator;
      UInt256 BlockHeaderHashDispatched;
      int BlocksDispachedCountTotal;

      IBlockPayloadParser BlockPayloadParser;



      public SessionBlockDownload(Blockchain blockchain, BlockPayloadLocator blockLocator, IBlockPayloadParser blockPayloadParser)
      {
        Blockchain = blockchain;
        BlockHeaderLocator = blockLocator;
        BlockPayloadParser = blockPayloadParser;
      }

      public override async Task StartAsync(BlockchainChannel channel)
      {
        Channel = channel;

        await DownloadBlocksAsync();
      }
      async Task DownloadBlocksAsync()
      {
        BlockHeaderHashDispatched = BlockHeaderLocator.GetBlockHeaderHash();

        while (BlockHeaderHashDispatched != null)
        {
          NetworkBlock networkBlock = await GetBlockDispatchedAsync();

          Validate(networkBlock, out IBlockPayload blockPayload);

          Blockchain.InsertBlock(blockPayload, BlockHeaderHashDispatched);

          blockPayload.StoreToDisk(BlockHeaderHashDispatched.ToString());

          Debug.WriteLine("Channel '{0}' downloaded block '{1}', Total blocks '{2}'", Channel.GetHashCode(), BlockHeaderHashDispatched.ToString(), ++BlocksDispachedCountTotal);

          BlockHeaderLocator.RemoveDispatched(BlockHeaderHashDispatched);
          BlockHeaderHashDispatched = BlockHeaderLocator.GetBlockHeaderHash();
        }
      }
      void Validate(NetworkBlock networkBlock, out IBlockPayload blockPayload)
      {
        UInt256 headerHash = new UInt256(Hashing.SHA256d(networkBlock.Header.getBytes()));

        if (!BlockHeaderHashDispatched.IsEqual(headerHash))
        {
          throw new BlockchainException(BlockCode.INVALID);
        }

        blockPayload = BlockPayloadParser.Parse(networkBlock.Payload);

        if (!blockPayload.GetPayloadHash().IsEqual(networkBlock.Header.PayloadHash))
        {
          throw new BlockchainException(BlockCode.INVALID);
        }
      }

      async Task<NetworkBlock> GetBlockDispatchedAsync()
      {
        await Channel.RequestBlockAsync(BlockHeaderHashDispatched).ConfigureAwait(false);

        //CancellationToken cancellationToken = new CancellationTokenSource(TimeSpan.FromSeconds(2)).Token;
        Network.BlockMessage blockMessage = await GetBlockMessageAsync().ConfigureAwait(false);

        return blockMessage.NetworkBlock;
      }

      async Task<Network.BlockMessage> GetBlockMessageAsync()
      {
        Network.BlockMessage blockMessage = await Channel.GetNetworkMessageAsync(default(CancellationToken)).ConfigureAwait(false) as Network.BlockMessage;

        return blockMessage ?? await GetBlockMessageAsync().ConfigureAwait(false);
      }
    }
  }
}
