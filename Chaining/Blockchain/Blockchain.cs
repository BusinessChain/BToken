using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Threading.Tasks.Dataflow;

using BToken.Networking;

namespace BToken.Chaining
{
  class Blockchain : Chain
  {
    Network Network;
    BufferBlock<NetworkMessage> NetworkMessageListener;

    Headerchain Headerchain;


    public Blockchain(ChainBlock genesisBlock, Network network) : base(genesisBlock)
    {
      Network = network;
      NetworkMessageListener = network.GetNetworkMessageListener();
      Headerchain = new Headerchain(genesisBlock.Header, network);
    }


    public async Task startAsync()
    {
      //await Headerchain.buildAsync();
      //await buildAsync();

      Task processMessagesUnsolicitedTask = ProcessMessagesUnsolicitedAsync();
    }
    async Task buildAsync()
    {
      List<UInt256> blocksMissing = getBlocksMissing();
      BufferBlock<NetworkBlock> networkBlockBuffer = Network.GetBlocks(blocksMissing);

      try
      {
        await insertNetworkBlocksAsync(networkBlockBuffer);
      }
      catch (ChainLinkException ex)
      {
        if (ex.HResult == (int)ChainLinkCode.DUPLICATE)
        {
          Network.duplicateHash(ex.ChainLink.Hash);
        }

        if (ex.HResult == (int)ChainLinkCode.ORPHAN)
        {
          Network.orphanBlockHash(ex.ChainLink.Hash);
        }

        await buildAsync();
      }
    }
    List<UInt256> getBlocksMissing()
    {
      return Headerchain.getHeaderLocator(getNextLocation);
    }
    uint getNextLocation(uint locator)
    {
      uint offsetChainDepths = Headerchain.getHeight() - getHeight();

      if (locator == offsetChainDepths)
      {
        return locator;
      }

      return locator++;
    }
    async Task insertNetworkBlocksAsync(BufferBlock<NetworkBlock> networkBlockBuffer)
    {
      NetworkBlock networkBlock = await networkBlockBuffer.ReceiveAsync();

      while (networkBlock != null)
      {
        insertNetworkBlock(networkBlock);

        networkBlock = await networkBlockBuffer.ReceiveAsync();
      }
    }
    void insertNetworkBlock(NetworkBlock networkBlock)
    {
      ChainBlock chainBlock = new ChainBlock(networkBlock);
      insertBlock(chainBlock);
    }
    void insertBlock(ChainBlock block)
    {
      insertChainLink(block);
    }
    List<TX> getTXs(NetworkBlock networkBlock)
    {
      throw new NotImplementedException();
    }


    async Task ProcessMessagesUnsolicitedAsync()
    {
      BufferBlock<InvMessage> invMessageBuffer = new BufferBlock<InvMessage>();

      while (true)
      {
        NetworkMessage message = await NetworkMessageListener.ReceiveAsync();

        switch (message)
        {
          case InvMessage invMessage:
            Console.WriteLine("'{0}' - invMessage, number of inventories = '{1}', type = '{2}', size = '{3}' Bytes", DateTimeOffset.UtcNow.ToString(), invMessage.GetInventoryCount(), invMessage.GetInventoryType(), invMessage.GetPayloadSize());
            invMessageBuffer.Post(invMessage);
            break;
          case HeadersMessage headersMessage:
            Console.WriteLine("headersMessage");
            break;
          default:
            break;
        }
      }
    }
  }
}
