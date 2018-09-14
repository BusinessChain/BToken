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

      BlockPayloadLocator BlockLocator;
      UInt256 BlockHashDispatched;

      int BlocksDispachtedCount = 0;



      public SessionBlockDownload(Blockchain blockchain, BlockPayloadLocator blockLocator)
      {
        Blockchain = blockchain;
        BlockLocator = blockLocator;
      }

      public override async Task StartAsync(BlockchainChannel channel)
      {
        Channel = channel;

        BlockHashDispatched = BlockLocator.GetBlockHash();

        while (BlockHashDispatched != null)
        {
          NetworkBlock block = await GetBlockDispatchedAsync();

          Blockchain.InsertBlock(block, BlockHashDispatched);

          Debug.WriteLine("Channel '{0}' dispatched block '{1}', Total blocks '{2}'", Channel.GetHashCode(), BlockHashDispatched.ToString(), ++BlocksDispachtedCount);

          BlockLocator.RemoveDispatched(BlockHashDispatched);
          BlockHashDispatched = BlockLocator.GetBlockHash();
        }
      }

      async Task<NetworkBlock> GetBlockDispatchedAsync()
      {
        await Channel.RequestBlockAsync(BlockHashDispatched);

        //CancellationToken cancellationToken = new CancellationTokenSource(TimeSpan.FromSeconds(2)).Token;
        Network.BlockMessage blockMessage = await GetBlockMessageAsync();
        NetworkBlock block = blockMessage.NetworkBlock;

        UInt256 headerHash = new UInt256(Hashing.SHA256d(block.Header.getBytes()));

        if (!BlockHashDispatched.IsEqual(headerHash))
        {
          throw new BlockchainException(BlockCode.INVALID);
        }

        return block;
      }

      async Task<Network.BlockMessage> GetBlockMessageAsync()
      {
        Network.BlockMessage blockMessage = await Channel.GetNetworkMessageAsync(default(CancellationToken)) as Network.BlockMessage;

        return blockMessage ?? await GetBlockMessageAsync();
      }
    }
  }
}
