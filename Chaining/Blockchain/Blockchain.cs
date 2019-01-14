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

    // maybe protect this field with LOCK prevents block download stall bug
    List<Task> BlockDownloadTasks = new List<Task>();
    const uint DOWNLOAD_TASK_COUNT_MAX = 8;


    public Blockchain(
      NetworkBlock genesisBlock,
      INetwork network,
      List<ChainLocation> checkpoints)
    {
      Network = network;
      Headers = new Headerchain(genesisBlock.Header, network, checkpoints, this);

      Archiver = new BlockArchiver(this);
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

    public BlockReader GetBlockReader()
    {
      return new BlockReader(this);
    }
    public async Task<List<NetworkBlock>> ReadBlocksAsync(byte[] headerIndex)
    {
      List<Headerchain.ChainHeader> headers = Headers.ReadHeaders(headerIndex);
      var blocks = new List<NetworkBlock>();

      foreach(var header in headers)
      {
        if(!Headerchain.TryGetHeaderHash(header, out UInt256 hash))
        {
          hash = header.NetworkHeader.ComputeHeaderHash();
        }
        NetworkBlock block = await Archiver.ReadBlockAsync(hash);
        ValidateHeader(hash, block);
        blocks.Add(block);
      }

      return blocks;
    }
       
    void ValidateHeader(UInt256 hash, NetworkBlock block)
    {
      UInt256 hashComputed = block.Header.ComputeHeaderHash();
      if (!hash.IsEqual(hashComputed))
      {
        throw new ChainException(ChainCode.INVALID);
      }
    }
  }
}
