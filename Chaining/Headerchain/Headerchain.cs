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
    Network Network;


    public Headerchain(ChainHeader genesisHeader, Network network) 
      : base(genesisHeader)
    {
      Network = network;
    }

    public async Task buildAsync()
    {
      //List<UInt256> headerLocator = getHeaderLocator();
      //BufferBlock<NetworkHeader> networkHeaderBuffer = new BufferBlock<NetworkHeader>();
      //Network.GetHeadersAsync(headerLocator);
      //await insertNetworkHeadersAsync(networkHeaderBuffer);
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

    public void RemoveExistingBlockHashInventories(List<Inventory> blockHashInventories)
    {
      for (int i = blockHashInventories.Count - 1; i >= 0; i--)
      {
        Inventory inventory = blockHashInventories[i];

        if(ContainsChainLinkHash(inventory.Hash))
        {
          blockHashInventories.RemoveAt(i);
        }
      }
    }

    public async Task insertNetworkHeadersAsync(BufferBlock<NetworkHeader> headerBuffer)
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
      insertChainLink(header);
    }

  }
}
