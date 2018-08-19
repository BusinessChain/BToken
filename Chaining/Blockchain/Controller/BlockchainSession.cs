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
      class BlockchainSession
      {
        BlockchainController Controller;

        BufferBlock<NetworkMessage> Buffer;

        public BlockchainSession(BufferBlock<NetworkMessage> buffer, BlockchainController controller)
        {
          Buffer = buffer;
          Controller = controller;
        }
        
        public async Task StartAsync()
        {
          while (true)
          {
            await ProcessNextMessageAsync();
          }
        }

        public async Task ProcessNextMessageAsync()
        {
          NetworkMessage networkMessage = await Buffer.ReceiveAsync();

          switch (networkMessage)
          {
            case InvMessage invMessage:
              ProcessInventoryMessage(invMessage);
              break;

            case HeadersMessage headersMessage:
              ProcessHeadersMessage(headersMessage);
              break;

            default:
              break;
          }
        }
        void ProcessInventoryMessage(InvMessage invMessage)
        {
          foreach (Inventory blockInventory in invMessage.GetBlockInventories())
          {
            ChainBlock chainBlock = Controller.Blockchain.GetChainBlock(blockInventory.Hash);

            if (chainBlock == null)
            {
              List<UInt256> headerLocator = Controller.Blockchain.getBlockLocator();
              Task getHeadersTask = Controller.Network.GetHeadersAsync(Buffer, headerLocator);
              return;
            }
            else
            {
              Controller.Network.BlameProtocolError(Buffer);
            }
          }
        }
        void ProcessHeadersMessage(HeadersMessage headersMessage)
        {
          foreach (NetworkHeader networkHeader in headersMessage.NetworkHeaders)
          {
            try
            {
              Controller.Blockchain.insertNetworkHeader(networkHeader);
            }
            catch (BlockchainException ex)
            {
              if (ex.ErrorCode == ChainLinkCode.DUPLICATE)
              {
                Controller.Network.BlameProtocolError(Buffer);
              }

              if (ex.ErrorCode == ChainLinkCode.ORPHAN)
              {
                Controller.Network.BlameProtocolError(Buffer);

                List<UInt256> headerLocator = Controller.Blockchain.getBlockLocator();
                Task getHeadersTask = Controller.Network.GetHeadersAsync(Buffer, headerLocator);
              }

              if (ex.ErrorCode == ChainLinkCode.INVALID)
              {
                Controller.Network.BlameConsensusError(Buffer);
              }

              return;
            }
          }

          if (headersMessage.NetworkHeaders.Any())
          {
            List<UInt256> headerLocator = Controller.Blockchain.getBlockLocator();
            Task getHeadersTask = Controller.Network.GetHeadersAsync(Buffer, headerLocator);
          }

          // check here if we have headers prior checkpoint.
        }

        public bool ContainsBuffer(BufferBlock<NetworkMessage> sessionMessageBuffer)
        {
          return sessionMessageBuffer == Buffer;
        }

      }
    }
  }
}
