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
    Headerchain Headerchain;
    BlockchainController Controller;


    public Blockchain(ChainBlock genesisBlock, UInt256 checkpointHash, Network network) : base(genesisBlock)
    {
      Headerchain = new Headerchain(genesisBlock.Header, checkpointHash , network);
      Controller = new BlockchainController(network, this);
    }


    public async Task startAsync()
    {
      //await Headerchain.buildAsync();
      //await buildAsync();

      await Controller.startAsync();
      Console.Write("helle");
    }
    async Task buildAsync()
    {
      List<UInt256> blocksMissing = getBlocksMissing();
      BufferBlock<NetworkBlock> networkBlockBuffer = null;//Network.GetBlocks(blocksMissing);

      try
      {
        await insertNetworkBlocksAsync(networkBlockBuffer);
      }
      catch (ChainLinkException ex)
      {
        if (ex.ErrorCode == ChainLinkCode.DUPLICATE)
        {
          //Network.duplicateHash(ex.ChainLink.Hash);
        }

        if (ex.ErrorCode == ChainLinkCode.ORPHAN)
        {
          //Network.orphanBlockHash(ex.ChainLink.Hash);
        }

        await buildAsync();
      }
    }
    List<UInt256> getBlocksMissing()
    {
      return Headerchain.getHeaderLocator();
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


  }
}
