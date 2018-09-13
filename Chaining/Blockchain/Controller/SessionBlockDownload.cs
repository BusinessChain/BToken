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

      List<UInt256> BlockLocations;
      List<NetworkBlock> BlocksDownloaded = new List<NetworkBlock>();



      public SessionBlockDownload(Blockchain blockchain, List<UInt256> blockLocations)
      {
        Blockchain = blockchain;
        BlockLocations = blockLocations;
      }

      public override async Task StartAsync(BlockchainChannel channel)
      {
        Channel = channel;

        for (int i = BlockLocations.Count - 1; i >= 0; i--)
        {
          UInt256 blockLocation = BlockLocations[i];
          BlocksDownloaded.Add(await GetBlockAsync(blockLocation));
          BlockLocations.RemoveAt(i);

          //Check if we received the block we requested.
          Debug.WriteLine("Channel " + Channel.GetHashCode());
        }

        InsertDownloadedBlocksInChain();
      }

      void InsertDownloadedBlocksInChain()
      {
        foreach (NetworkBlock block in BlocksDownloaded)
        {
          UInt256 headerHash = new UInt256(Hashing.SHA256d(block.Header.getBytes()));

          try
          {
            Blockchain.InsertBlock(block, headerHash);
          }
          catch (BlockchainException ex)
          {
            Debug.WriteLine("Block insertion failed, Channel " + Channel.GetHashCode() + ", block hash: " + headerHash + "\nException: " + ex.Message);
          }
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
