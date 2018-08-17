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
      class NetworkMessageSession
      {
        BlockchainController Controller;

        BufferBlock<NetworkMessage> Buffer;
        NetworkMessage NetworkMessageReceivedOld;
        NetworkMessage NetworkMessageReceivedNext;

        public NetworkMessageSession(BufferBlock<NetworkMessage> buffer, BlockchainController controller)
        {
          Buffer = buffer;
          Controller = controller;
        }

        public async Task ProcessNextMessageAsync()
        {
          NetworkMessageReceivedNext = await Buffer.ReceiveAsync();

          switch (NetworkMessageReceivedNext)
          {
            case InvMessage invMessage:
              await ProcessInventoryMessageAsync(invMessage);
              break;

            case HeadersMessage headersMessage:
              await ProcessHeadersMessageAsync(headersMessage);
              break;

            default:
              break;
          }

          NetworkMessageReceivedOld = NetworkMessageReceivedNext;
        }
        async Task ProcessInventoryMessageAsync(InvMessage invMessage)
        {
          foreach (Inventory blockInventory in invMessage.GetBlockInventories())
          {
            ChainBlock chainHeader = Controller.Blockchain.GetChainBlock(blockInventory.Hash);

            if (chainHeader != null)
            {
              Controller.Network.BlameProtocolError(Buffer);
            }
            else
            {
              List<UInt256> headerLocator = Controller.Blockchain.getBlockLocator();
              await Controller.Network.GetHeadersAsync(Buffer, headerLocator);
              return;
            }
          }
        }
        async Task ProcessHeadersMessageAsync(HeadersMessage headersMessage)
        {
          foreach (NetworkHeader networkHeader in headersMessage.NetworkHeaders)
          {
            try
            {
              Controller.Blockchain.insertNetworkHeader(networkHeader);
            }
            catch (ChainLinkException ex)
            {
              if (ex.ErrorCode == ChainLinkCode.DUPLICATE)
              {
                Controller.Network.BlameProtocolError(Buffer);
              }

              if (ex.ErrorCode == ChainLinkCode.ORPHAN)
              {
                Controller.Network.BlameProtocolError(Buffer);

                List<UInt256> headerLocator = Controller.Blockchain.getBlockLocator();
                await Controller.Network.GetHeadersAsync(Buffer, headerLocator);
                return;
              }

              if (ex.ErrorCode == ChainLinkCode.INVALID)
              {
                Controller.Network.BlameConsensusError(Buffer);
              }
            }
          }

          if (headersMessage.NetworkHeaders.Any())
          {
            List<UInt256> headerLocator = Controller.Blockchain.getBlockLocator();
            await Controller.Network.GetHeadersAsync(Buffer, headerLocator);
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
