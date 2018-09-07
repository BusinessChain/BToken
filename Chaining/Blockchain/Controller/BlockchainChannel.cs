using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Threading.Tasks.Dataflow;

using BToken.Networking;

namespace BToken.Chaining
{
  partial class Blockchain
  {
    partial class BlockchainController
    {
      partial class BlockchainChannel
      {
        BlockchainController Controller;
        public BufferBlock<NetworkMessage> Buffer;
        

        public BlockchainChannel() { }
        public BlockchainChannel(BufferBlock<NetworkMessage> buffer, BlockchainController controller)
        {
          Buffer = buffer;
          Controller = controller;
        }
        
        public async Task StartMessageListenerAsync()
        {
          try
          {
            while (true)
            {
              await ProcessNextMessageAsync();
            }
          }
          catch
          {
            BlockchainChannel channel = await Controller.RenewChannelAsync(this);
            Task startChannelTask = channel.StartMessageListenerAsync();
          }
        }
        
        public async Task ExecuteSessionAsync(BlockchainSession session)
        {
          try
          {
            await session.StartAsync(this);
          }
          catch
          {
            BlockchainChannel channel = await Controller.RenewChannelAsync(this);
            await channel.ExecuteSessionAsync(session);
          }
        }

        async Task ProcessNextMessageAsync()
        {
          NetworkMessage networkMessage = await GetNetworkMessageAsync(default(CancellationToken));

          switch (networkMessage)
          {
            case InvMessage invMessage:
              //await ProcessInventoryMessageAsync(invMessage);
              break;

            case Network.HeadersMessage headersMessage:
              //await ExecuteSessionAsync(new SessionHeaderDownload(headersMessage));
              break;

            case Network.BlockMessage blockMessage:
              break;

            default:
              break;
          }
        }
        public async Task<NetworkMessage> GetNetworkMessageAsync(CancellationToken cancellationToken)
        {
          NetworkMessage networkMessage = await Buffer.ReceiveAsync(cancellationToken);

          return networkMessage ?? throw new NetworkException("Network closed channel."); ;
        }
        
        async Task ProcessInventoryMessageAsync(InvMessage invMessage)
        {
          foreach (Inventory blockInventory in invMessage.GetBlockInventories())
          {
            ChainBlock chainBlock = Controller.Blockchain.GetBlock(blockInventory.Hash);

            if (chainBlock == null)
            {
              await RequestHeadersAsync(GetHeaderLocator());
              return;
            }
            else
            {
              BlameProtocolError();
            }
          }
        }

        public async Task RequestHeadersAsync(List<BlockLocation> headerLocator) => await Controller.Network.GetHeadersAsync(Buffer, headerLocator.Select(b => b.Hash).ToList());
        List<BlockLocation> GetHeaderLocator() => Controller.Blockchain.GetHeaderLocator();
                
        public async Task DownloadBlocksAsync(List<List<BlockLocation>> blockLocationBatches)
        {
          while (blockLocationBatches.Any())
          {
            List<BlockLocation> blockLocations = blockLocationBatches[0];
            blockLocationBatches.RemoveAt(0);

            //await new SessionBlockDownload(this).StartAsync(blockLocations);
          }
        }
        public async Task RequestBlockAsync(UInt256 blockHash) => await Controller.Network.GetBlockAsync(Buffer, new List<UInt256> { blockHash });

        void BlameConsensusError()
        {
          Controller.Network.BlameConsensusError(Buffer);
        }
        void BlameProtocolError()
        {
          Controller.Network.BlameProtocolError(Buffer);
        }
      }
    }
  }
}
