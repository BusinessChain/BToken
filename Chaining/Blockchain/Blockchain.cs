using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using BToken.Networking;

namespace BToken.Chaining
{
  public class Blockchain : IBlockchain
  {
    Headerchain Headerchain;
    IPayloadParser PayloadParser;
    INetwork Network;


    public Blockchain(
      Headerchain.ChainHeader genesisBlock,
      INetwork network,
      List<ChainLocation> checkpoints,
      IPayloadParser payloadParser)
    {
      Network = network;
      Headerchain = new Headerchain(genesisBlock, network, checkpoints, this);
      PayloadParser = payloadParser;
    }

    public async Task StartAsync()
    {
      await Headerchain.StartAsync();
    }

    public async Task InitialBlockDownloadAsync()
    {

    }

    public void DownloadBlock(NetworkHeader header)
    {

    }

  }
}
