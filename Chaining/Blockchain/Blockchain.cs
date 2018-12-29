using System;
using System.Collections.Generic;
using System.Linq;
using System.IO;
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
    IPayloadParser PayloadParser;

    // maybe protect this field with LOCK prevents block download stall bug
    List<Task> BlockDownloadTasks = new List<Task>();
    const uint DOWNLOAD_TASK_COUNT_MAX = 8;


    public Blockchain(
      NetworkBlock genesisBlock,
      INetwork network,
      List<ChainLocation> checkpoints,
      IPayloadParser payloadParser)
    {
      Network = network;
      Headers = new Headerchain(genesisBlock.Header, network, checkpoints, this);

      Archiver = new BlockArchiver(this);
      Listener = new BlockchainRequestListener(this, network);
      PayloadParser = payloadParser;
    }

    public async Task StartAsync()
    {
      await Headers.LoadFromArchiveAsync();
      Console.WriteLine("Loaded headerchain from archive, height = '{0}'", Headers.GetHeight());

      Task listenerTask = Listener.StartAsync();
      Console.WriteLine("Inbound request listener started...");

      await Headers.InitialHeaderDownloadAsync();
      Console.WriteLine("Synchronized headerchain with network, height = '{0}'", Headers.GetHeight());

      //Task initialBlockDownloadTask = InitialBlockDownloadAsync(Headers.GetHeaderStreamer());
    }
    //async Task InitialBlockDownloadAsync(Headerchain.HeaderStream headerStreamer)
    //{
    //  ChainLocation headerLocation = headerStreamer.ReadHeaderLocationTowardGenesis();
    //  while (headerLocation != null)
    //  {
    //    if (!Archiver.BlockExists(headerLocation.Hash))
    //    {
    //      await AwaitNextDownloadTask();
    //      PostBlockDownloadSession(headerLocation);
    //    }

    //    headerLocation = headerStreamer.ReadHeaderLocationTowardGenesis();
    //  }

    //  Console.WriteLine("Synchronizing blocks with network completed.");
    //}
    async Task AwaitNextDownloadTask()
    {
      if (BlockDownloadTasks.Count < DOWNLOAD_TASK_COUNT_MAX)
      {
        return;
      }
      else
      {
        Task blockDownloadTaskCompleted = await Task.WhenAny(BlockDownloadTasks);
        BlockDownloadTasks.Remove(blockDownloadTaskCompleted);
      }
    }
    void PostBlockDownloadSession(ChainLocation headerLocation)
    {
      var sessionBlockDownload = new SessionBlockDownload(headerLocation, this);

      Task executeSessionTask = Network.ExecuteSessionAsync(sessionBlockDownload);
      BlockDownloadTasks.Add(executeSessionTask);
    }

    public BlockStream GetBlockStream()
    {
      return new BlockStream(this);
    }
       
    void ValidateBlock(UInt256 hash, NetworkBlock block)
    {
      UInt256 headerHash = block.Header.GetHeaderHash();
      if (!hash.IsEqual(headerHash))
      {
        throw new ChainException(HeaderCode.INVALID);
      }

      UInt256 payloadHash = PayloadParser.GetPayloadHash(block.Payload);
      if (!payloadHash.IsEqual(block.Header.MerkleRoot))
      {
        throw new ChainException(HeaderCode.INVALID);
      }
    }
  }
}
