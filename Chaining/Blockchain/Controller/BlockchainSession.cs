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
      partial class BlockchainSession
      {
        BlockchainController Controller;
        public BufferBlock<NetworkMessage> Buffer;
        

        public BlockchainSession() { }
        public BlockchainSession(BufferBlock<NetworkMessage> buffer, BlockchainController controller)
        {
          Buffer = buffer;
          Controller = controller;
        }
        
        public async Task StartAsync()
        {
          try
          {
            while (true)
            {
              await ProcessNextSessionAsync();
            }
          }
          catch
          {
            await Controller.RenewSessionAsync(this);
          }
        }
        public async Task TriggerHeaderDownloadAsync() => await RequestHeadersAsync(GetHeaderLocator());
        
        async Task ProcessNextSessionAsync()
        {
          NetworkMessage networkMessage = await GetNetworkMessageAsync(default(CancellationToken));

          switch (networkMessage)
          {
            case InvMessage invMessage:
              //await ProcessInventoryMessageAsync(invMessage);
              break;

            case Network.HeadersMessage headersMessage:
              await new HeadersSession(this).StartAsync(headersMessage);
              break;

            case Network.BlockMessage blockMessage:
              break;

            default:
              break;
          }
        }
        async Task<NetworkMessage> GetNetworkMessageAsync(CancellationToken cancellationToken)
        {
          NetworkMessage networkMessage = await Buffer.ReceiveAsync(cancellationToken);

          return networkMessage ?? throw new NetworkException("Network disposed session."); ;
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

        async Task RequestHeadersAsync(List<BlockLocation> headerLocator) => await Controller.Network.GetHeadersAsync(Buffer, headerLocator.Select(b => b.Hash).ToList());
        List<BlockLocation> GetHeaderLocator() => Controller.Blockchain.GetHeaderLocator();
                
        public async Task DownloadBlocksAsync(List<List<BlockLocation>> blockLocationBatches)
        {
          while (blockLocationBatches.Any())
          {
            List<BlockLocation> blockLocations = blockLocationBatches[0];
            blockLocationBatches.RemoveAt(0);

            await new BlockSession(this).StartAsync(blockLocations);
          }
        }
        async Task RequestBlockAsync(UInt256 blockHash) => await Controller.Network.GetBlockAsync(Buffer, new List<UInt256> { blockHash });

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
