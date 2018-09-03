using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

using BToken.Networking;

namespace BToken.Chaining
{
  partial class Blockchain
  {
    partial class BlockchainController
    {
      partial class BlockchainSession
      {
        class BlockSession
        {
          BlockchainSession BlockchainSession;


          public BlockSession(BlockchainSession blockchainSession)
          {
            BlockchainSession = blockchainSession;
          }

          public async Task StartAsync(List<BlockLocation> blockLocations)
          {
            var blocks = new List<NetworkBlock>();

            foreach(BlockLocation blockLocation in blockLocations)
            {
              NetworkBlock block = await GetBlockAsync(blockLocation.Hash);
              blocks.Add(block);
            }
          }

          async Task<NetworkBlock> GetBlockAsync(UInt256 blockHash)
          {
            await BlockchainSession.RequestBlockAsync(blockHash);

            //CancellationToken cancellationToken = new CancellationTokenSource(TimeSpan.FromSeconds(2)).Token;
            Network.BlockMessage blockMessage = await GetBlockMessageAsync();

            return blockMessage.NetworkBlock;
          }

          async Task<Network.BlockMessage> GetBlockMessageAsync()
          {
            Network.BlockMessage blockMessage = await BlockchainSession.GetNetworkMessageAsync(default(CancellationToken)) as Network.BlockMessage;

            return blockMessage ?? await GetBlockMessageAsync();
          }
        }
      }
    }
  }
}
