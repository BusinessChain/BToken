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
            await TriggerBlockDownloadAsync();

            while (true)
            {
              await ProcessNextSessionAsync();
            }
          }
          catch
          {
            Controller.DisposeSession(this);
          }
        }
        async Task TriggerBlockDownloadAsync() => await RequestHeadersAsync(GetHeaderLocator());



        async Task ProcessNextSessionAsync()
        {
          NetworkMessage networkMessage = await GetNetworkMessageAsync(default(CancellationToken));

          switch (networkMessage)
          {
            case InvMessage invMessage:
              //await ProcessInventoryMessageAsync(invMessage);
              break;

            case HeadersMessage headersMessage:
              await new HeadersSession(this).StartAsync(headersMessage);
              await new BlockDownloadSession(this).StartAsync();
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
