using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using BToken.Networking;

namespace BToken.Chaining
{
  partial class Blockchain : Chain
  {
    class BlockchainController
    {
      const int BLAME_FACTOR_DUPLICATE_BLOCKHASH = 20;

      Network Network;
      Blockchain Blockchain;


      public BlockchainController(Network network, Blockchain blockchain)
      {
        Network = network;
        Blockchain = blockchain;
      }

      public async Task startAsync()
      {
        await ProcessNetworkMessagesIncomingAsync();
      }
      async Task ProcessNetworkMessagesIncomingAsync()
      {
        while (true)
        {
          NetworkMessage networkMessage = await Network.ReceiveBlockchainNetworkMessageAsync();

          switch (networkMessage)
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
        }
      }
      async Task ProcessHeadersMessageAsync(HeadersMessage headersMessage)
      {
        try
        {
          Blockchain.Headerchain.insertNetworkHeaders(headersMessage.NetworkHeaders);
        }
        catch (ChainLinkException ex)
        {
          if (ex.ErrorCode == ChainLinkCode.DUPLICATE)
          {
            Network.duplicateHash(headersMessage, ex.ChainLink.Hash);
          }

          if (ex.ErrorCode == ChainLinkCode.ORPHAN)
          {
            List<UInt256> headerLocator = Blockchain.Headerchain.getHeaderLocator();
            await Network.RequestOrphanParentHeadersAsync(headersMessage, ex.ChainLink.Hash, headerLocator);
          }
        }

      }
      async Task ProcessInventoryMessageAsync(InvMessage invMessage)
      {
        List<Inventory> blockHashInventories = invMessage.GetInventoriesBlock();

        uint depthOfDuplicatesSum = RemoveInventoriesDuplicate(blockHashInventories);

        if (depthOfDuplicatesSum > 0)
        {
          uint blameScore = depthOfDuplicatesSum * BLAME_FACTOR_DUPLICATE_BLOCKHASH;
          Network.BlameMessage(invMessage, blameScore);
        }

        if (blockHashInventories.Any())
        {
          IEnumerable<UInt256> headerLocator = blockHashInventories.Select(i => i.Hash);
          await Network.GetHeadersAdvertisedAsync(invMessage, headerLocator);
        }
      }
      uint RemoveInventoriesDuplicate(List<Inventory> blockHashInventories)
      {
        uint depthOfDuplicatesSum = 0;

        for (int i = blockHashInventories.Count - 1; i >= 0; i--)
        {
          Inventory inventory = blockHashInventories[i];
          ChainHeader chainHeader = Blockchain.Headerchain.GetChainHeader(inventory.Hash);

          if (chainHeader != null)
          {
            blockHashInventories.RemoveAt(i);
            uint chainHeaderDepth = Blockchain.Headerchain.getHeight() - chainHeader.Height;
            depthOfDuplicatesSum += chainHeaderDepth;
          }
        }

        return depthOfDuplicatesSum;
      }
    }
  }
}
