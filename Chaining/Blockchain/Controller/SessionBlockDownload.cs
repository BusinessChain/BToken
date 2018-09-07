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
  partial class Blockchain
  {
    partial class BlockchainController
    {
      class SessionBlockDownload : BlockchainSession
      {
        Blockchain Blockchain;

        List<BlockLocation> BlockLocations;
        List<NetworkBlock> BlocksDownloaded = new List<NetworkBlock>();



        public SessionBlockDownload(Blockchain blockchain, List<BlockLocation> blockLocations)
        {
          Blockchain = blockchain;
          BlockLocations = blockLocations;
        }

        public override async Task StartAsync(BlockchainChannel channel)
        {
          Channel = channel;

          foreach (BlockLocation blockLocation in BlockLocations)
          {
            NetworkBlock block = await GetBlockAsync(blockLocation.Hash);
            BlocksDownloaded.Add(block);
          }
        }

        async Task<NetworkBlock> GetBlockAsync(UInt256 blockHash)
        {
          await Channel.RequestBlockAsync(blockHash);

          //CancellationToken cancellationToken = new CancellationTokenSource(TimeSpan.FromSeconds(2)).Token;
          Network.BlockMessage blockMessage = await GetBlockMessageAsync();

          return blockMessage.NetworkBlock;
        }

        async Task<Network.BlockMessage> GetBlockMessageAsync()
        {
          Network.BlockMessage blockMessage = await Channel.GetNetworkMessageAsync(default(CancellationToken)) as Network.BlockMessage;

          return blockMessage ?? await GetBlockMessageAsync();
        }
      }
    }
  }
}
