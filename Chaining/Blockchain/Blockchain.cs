using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

using BToken.Networking;

namespace BToken.Chaining
{
  public partial class Blockchain : IBlockchain
  {
    Headerchain Headerchain;
    static IPayloadParser PayloadParser;
    INetwork Network;
    BlockArchiver Archiver;


    public Blockchain(
      NetworkBlock genesisBlock,
      INetwork network,
      List<ChainLocation> checkpoints,
      IPayloadParser payloadParser)
    {
      Network = network;
      Headerchain = new Headerchain(genesisBlock.Header, network, checkpoints, this);
      PayloadParser = payloadParser;

      Archiver = new BlockArchiver(this, network);
    }

    public async Task StartAsync()
    {
      await Headerchain.StartAsync();
    }

    public async Task InitialBlockDownloadAsync()
    {
      await Archiver.InitialBlockDownloadAsync();
    }

    public void DownloadBlock(NetworkHeader header)
    {

    }

    public async Task<INetworkSession> RequestSessionAsync(NetworkMessage networkMessage, CancellationToken cancellationToken)
    {
      switch (networkMessage)
      {
        //case InvMessage invMessage:
        //await ProcessInventoryMessageAsync(invMessage);
        //break;

        case Network.HeadersMessage headersMessage:
          var location = new ChainLocation(0, null);
          return new SessionBlockDownload(Archiver, location);

        case Network.BlockMessage blockMessage:
          location = new ChainLocation(0, null);
          return new SessionBlockDownload(Archiver, location);

        default:
          return null;
      }
    }

  }
}
