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
    UInt256 CheckpointHash;


    public Headerchain(ChainHeader genesisHeader, UInt256 checkpointHash, Network network) 
      : base(genesisHeader)
    {
      CheckpointHash = checkpointHash;
      Network = network;
    }

    public async Task buildAsync()
    {
      //List<UInt256> headerLocator = getHeaderLocator();
      //BufferBlock<NetworkHeader> networkHeaderBuffer = new BufferBlock<NetworkHeader>();
      //Network.GetHeadersAsync(headerLocator);
      //await insertNetworkHeadersAsync(networkHeaderBuffer);
    }

    public List<UInt256> getHeaderLocator()
    {
      uint getNextLocation(uint locator)
      {
        if (locator < 10)
          return locator + 1;
        else
          return locator * 2;
      }

      return getChainLinkLocator(CheckpointHash, getNextLocation);
    }

    public ChainHeader GetChainHeader(UInt256 hash)
    {
      return (ChainHeader)GetChainLink(hash);
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
    public void insertNetworkHeader(NetworkHeader networkHeader)
    {
      ChainHeader chainHeader = new ChainHeader(networkHeader);
      insertChainLink(chainHeader);
    }

  }
}
