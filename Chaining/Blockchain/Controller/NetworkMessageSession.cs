using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Threading.Tasks.Dataflow;

using BToken.Networking;

namespace BToken.Chaining
{
  partial class Blockchain : Chain
  {
    partial class BlockchainController
    {
      class NetworkMessageSession
      {
        BlockchainController Controller;

        BufferBlock<NetworkMessage> Buffer;
        NetworkMessage NetworkMessageOld;
        NetworkMessage NetworkMessageNext;

        public NetworkMessageSession(BufferBlock<NetworkMessage> buffer, BlockchainController controller)
        {
          Buffer = buffer;
          Controller = controller;
        }
        
        public async Task ProcessNextMessageAsync()
        {
          NetworkMessageNext = await Buffer.ReceiveAsync();

          switch (NetworkMessageNext)
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

          NetworkMessageOld = NetworkMessageNext;
        }
        async Task ProcessInventoryMessageAsync(InvMessage invMessage)
        {
          foreach (Inventory blockInventory in invMessage.GetBlockInventories())
          {
            ChainHeader chainHeader = Controller.Blockchain.Headerchain.GetChainHeader(blockInventory.Hash);

            if (chainHeader != null)
            {
              uint chainHeaderDepth = Controller.Blockchain.Headerchain.getHeight() - chainHeader.Height;
              uint blameScore = chainHeaderDepth * BLAME_FACTOR_DUPLICATE_BLOCKHASH;
              Controller.Network.BlameMessage(invMessage, blameScore);
            }
            else
            {
              await RequestNewHeaderAdvertised(invMessage);
              return;
            }
          }
        }
        async Task RequestNewHeaderAdvertised(InvMessage invMessage)
        {
          List<UInt256> headerLocator = Controller.Blockchain.Headerchain.getHeaderLocator();
          await Controller.Network.GetHeadersAdvertisedAsync(invMessage, headerLocator);
      }
        async Task ProcessHeadersMessageAsync(HeadersMessage headersMessage)
        {
          foreach (NetworkHeader networkHeader in headersMessage.NetworkHeaders)
          {
            try
            {
              Controller.Blockchain.Headerchain.insertNetworkHeader(networkHeader);
            }
            catch (ChainLinkException ex)
            {
              if (ex.ErrorCode == ChainLinkCode.DUPLICATE)
              {
                uint chainHeaderDepth = Controller.Blockchain.Headerchain.getHeight() - ex.ChainLink.Height;
                uint blameScore = chainHeaderDepth * BLAME_FACTOR_DUPLICATE_BLOCKHASH;
                Controller.Network.BlameMessage(headersMessage, blameScore);
              }

              if (ex.ErrorCode == ChainLinkCode.ORPHAN)
              {
                List<UInt256> headerLocator = Controller.Blockchain.Headerchain.getHeaderLocator();
                await Controller.Network.GetHeadersAdvertisedAsync(headersMessage, headerLocator);
                return;
              }

              // What happens if invalid ??
            }
          }

          if (headersMessage.NetworkHeaders.Any())
          {
            List<UInt256> headerLocator = Controller.Blockchain.Headerchain.getHeaderLocator();
            await Controller.Network.GetHeadersAdvertisedAsync(headersMessage, headerLocator);
            return;
          }
        }

        public bool ContainsBuffer(BufferBlock<NetworkMessage> sessionMessageBuffer)
        {
          return sessionMessageBuffer == Buffer;
        }

      }
    }
  }
}
