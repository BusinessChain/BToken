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
    NetworkAdapter NetworkAdapter;

    Headerchain Headerchain;


    public Blockchain(ChainBlock genesisBlock, NetworkAdapter networkAdapter) : base(genesisBlock)
    {
      NetworkAdapter = networkAdapter;
      Headerchain = new Headerchain(genesisBlock.Header, networkAdapter);
    }


    public async Task startAsync()
    {
      await Headerchain.buildAsync();
      await buildAsync();
    }
    async Task buildAsync()
    {
      List<UInt256> blocksMissing = getBlocksMissing();
      BufferBlock<NetworkBlock> networkBlockBuffer = NetworkAdapter.GetBlocks(blocksMissing);

      try
      {
        await insertNetworkBlocksAsync(networkBlockBuffer);
      }
      catch (ChainLinkException ex)
      {
        if (ex.HResult == (int)ChainLinkCode.DUPLICATE)
        {
          NetworkAdapter.duplicateHash(ex.ChainLink.getHash());
        }

        if (ex.HResult == (int)ChainLinkCode.ORPHAN)
        {
          NetworkAdapter.orphanBlockHash(ex.ChainLink.getHash());
        }
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
      try
      {
        insertChainLink(block);
      }
      catch (ChainLinkException ex)
      {
        if (ex.HResult == (int)ChainLinkCode.DUPLICATE)
        {
          NetworkAdapter.duplicateHash(block.getHash());
        }

        if (ex.HResult == (int)ChainLinkCode.ORPHAN)
        {
          NetworkAdapter.orphanBlockHash(block.getHash());
        }
      }
    }
    List<TX> getTXs(NetworkBlock networkBlock)
    {
      throw new NotImplementedException();
    }
    void validateBlock(ChainBlock blockPrevious, ChainBlock block)
    {
      if (blockPrevious.isConnectedToNext(block))
      {
        NetworkAdapter.duplicateHash(block.getHash());
        return;
      }

      if (!block.getHash().isEqual(block.getChainLinkPrevious().getHash()))
      {
        throw new InvalidOperationException("New block does not link to previous block hash.");
      }
    }

    public uint getBestHeight()
    {
      return getHeight();
    }
  }
}
