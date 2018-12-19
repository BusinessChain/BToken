using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

using BToken.Networking;

namespace BToken.Chaining
{
  public partial class Blockchain
  {
    Headerchain Headers;
    INetwork Network;
    BlockArchiver Archiver;
    BlockchainRequestListener Listener;


    public Blockchain(
      NetworkBlock genesisBlock,
      INetwork network,
      List<ChainLocation> checkpoints,
      IPayloadParser payloadParser)
    {
      Network = network;
      Headers = new Headerchain(genesisBlock.Header, network, checkpoints, this);

      Archiver = new BlockArchiver(payloadParser, this, network);
      Listener = new BlockchainRequestListener(this, network);
    }

    public async Task StartAsync()
    {
      await Headers.LoadFromArchiveAsync();
      Console.WriteLine("Loaded headerchain from archive, height = '{0}'", Headers.GetHeight());

      Task listenerTask = Listener.StartAsync();
      Console.WriteLine("Inbound request listener started...");

      await Headers.InitialHeaderDownloadAsync();
      Console.WriteLine("Synchronized headerchain with network, height = '{0}'", Headers.GetHeight());

      Task initialBlockDownloadTask = Archiver.InitialBlockDownloadAsync(Headers.GetHeaderStreamer());
    }

    public BlockStream GetBlockStream()
    {
      return new BlockStream(this);
    }
    public async Task<NetworkBlock> GetBlockAsync(UInt256 headerHash)
    {
      return await Archiver.ReadBlockAsync(headerHash);
    }
  }
}
