using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Threading.Tasks.Dataflow;

using BToken.Networking;

namespace BToken.Chaining
{
  partial class Headerchain : Chain
  {
    NetworkAdapter NetworkAdapter;


    public Headerchain(ChainHeader genesisHeader, NetworkAdapter networkAdapter) 
      : base(genesisHeader)
    {
      NetworkAdapter = networkAdapter;
    }

    public async Task buildAsync()
    {
      List<UInt256> headerLocator = getHeaderLocator();
      BufferBlock<NetworkHeader> networkHeaderBuffer = NetworkAdapter.GetHeaders(headerLocator);
      await insertNetworkHeadersAsync(networkHeaderBuffer);
    }
    public List<UInt256> getHeaderLocator(Func<uint, uint> getNextLocation)
    {
      return getChainLinkLocator(getNextLocation);
    }
    public List<UInt256> getHeaderLocator()
    {
      return getChainLinkLocator(getNextLocation);
    }
    uint getNextLocation(uint locator)
    {
      if (locator < 10)
      {
        return locator + 1;
      }
      else
      {
        return locator << 1;
      }
    }

    async Task insertNetworkHeadersAsync(BufferBlock<NetworkHeader> headerBuffer)
    {
      NetworkHeader networkHeader = await headerBuffer.ReceiveAsync();

      while (networkHeader != null)
      {
        insertNetworkHeader(networkHeader);

        networkHeader = await headerBuffer.ReceiveAsync();
      }
    }
    void insertNetworkHeader(NetworkHeader networkHeader)
    {
      ChainHeader chainHeader = new ChainHeader(networkHeader);
      insertHeader(chainHeader);
    }

    void insertHeader(ChainHeader header)
    {
      try
      {
        insertChainLink(header);
      }
      catch (ChainLinkException ex)
      {
        if(ex.HResult == (int)ChainLinkCode.DUPLICATE)
        {
          NetworkAdapter.duplicateHash(header.Hash);
        }

        if (ex.HResult == (int)ChainLinkCode.ORPHAN)
        {
          NetworkAdapter.orphanHeaderHash(header.Hash);
        }
      }
    }

    public async Task readMessageAsync()
    {
      while (true)
      {
        NetworkMessage networkMessage = await NetworkAdapter.readMessageAsync();

        switch (networkMessage)
        {
          //case GetBlocksMessage getBlocksMessage:
          // Message handlen
          // break;
          // case BlockMessage blockMessage:
          // Message handlen
          // return new BlockPayloadMessage : BlockchainMessage
          // break;
          default:
            throw new NotSupportedException("Blockchain received unknown NetworkMessage from NetworkAdapter.");
        }
      }
    }

  }
}
