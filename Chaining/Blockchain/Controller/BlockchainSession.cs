using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
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
            await TriggerHeadersSessionAsync();

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
        async Task TriggerHeadersSessionAsync() => await GetHeadersAsync();

               
        async Task ProcessNextSessionAsync()
        {
          NetworkMessage networkMessage = await GetNextNetworkMessageAsync();

          switch (networkMessage)
          {
            case InvMessage invMessage:
              await ProcessInventoryMessageAsync(invMessage);
              break;

            case HeadersMessage headersMessage:
              await new HeadersSession(this).StartAsync(headersMessage);
              break;

            default:
              break;
          }
        }
        async Task<NetworkMessage> GetNextNetworkMessageAsync()
        {
          NetworkMessage networkMessage = await Buffer.ReceiveAsync();
          if (networkMessage == null)
          {
            throw new NetworkException("Network disposed session.");
          }

          return networkMessage;
        }
        async Task ProcessInventoryMessageAsync(InvMessage invMessage)
        {
          foreach (Inventory blockInventory in invMessage.GetBlockInventories())
          {
            ChainBlock chainBlock = Controller.Blockchain.GetBlock(blockInventory.Hash);

            if (chainBlock == null)
            {
              await GetHeadersAsync();
              return;
            }
            else
            {
              BlameProtocolError();
            }
          }
        }
        
        async Task GetHeadersAsync()
        {
          List<UInt256> blockLocator = Controller.Blockchain.GetBlockLocator().Select(b => b.Hash).ToList();
          await Controller.Network.GetHeadersAsync(Buffer, blockLocator);
        }

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
