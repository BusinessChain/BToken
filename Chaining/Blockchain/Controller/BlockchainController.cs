using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Threading.Tasks.Dataflow;

using BToken.Networking;

namespace BToken.Chaining
{
  partial class Blockchain
  {
    partial class BlockchainController
    {
      Network Network;
      Blockchain Blockchain;

      const int CHANNELS_COUNT = 16;
      List<BlockchainChannel> Channels = new List<BlockchainChannel>();
      

      public BlockchainController(Network network, Blockchain blockchain)
      {
        Network = network;
        Blockchain = blockchain;
      }

      public async Task StartAsync()
      {
        var createChannelTasks = new List<Task<BlockchainChannel>>();

        for (int i = 0; i < CHANNELS_COUNT; i++)
        {
          createChannelTasks.Add(CreateChannelAsync());
        }
        
        await Task.WhenAny(createChannelTasks);
        await Channels.First().ExecuteSessionAsync(new SessionHeaderDownload(Blockchain));
      }
      async Task<BlockchainChannel> CreateChannelAsync()
      {
        BufferBlock<NetworkMessage> buffer = await Network.CreateBlockchainChannelAsync(Blockchain.GetHeight());
        BlockchainChannel channel = new BlockchainChannel(buffer, this);
        Channels.Add(channel);
        return channel;
      }

      async Task RequestBlockDownloadAsync()
      {
        // Use concurrent collection instead of list
        List<List<BlockLocation>> blockLocationBatches = CreateBlockLocationBatches();

        var downloadBlocksTasks = new List<Task>();
        foreach(BlockchainChannel channel in Channels)
        {
          downloadBlocksTasks.Add(channel.DownloadBlocksAsync(blockLocationBatches));
        }
        await Task.WhenAll(downloadBlocksTasks);
      }
      List<List<BlockLocation>> CreateBlockLocationBatches()
      {
        var blockLocationBatches = new List<List<BlockLocation>>();
        ChainSocket socket = Blockchain.SocketMain;

        while (socket != null)
        {
          socket.Reset();

          CreateBlockLocationBatchesPerSocket(socket.Probe, blockLocationBatches);

          socket = socket.WeakerSocket;
        }

        return blockLocationBatches;
      }
      void CreateBlockLocationBatchesPerSocket(ChainSocket.SocketProbe socketProbe, List<List<BlockLocation>> blockLocationBatches)
      {
        bool finalBatch = false;

        while (!finalBatch)
        {
          List<BlockLocation> blockLocationBatch = CreateBlockLocationBatch(socketProbe, out finalBatch);
          blockLocationBatches.Add(blockLocationBatch);
        }
      }
      List<BlockLocation> CreateBlockLocationBatch(ChainSocket.SocketProbe socketProbe, out bool finalList)
      {
        const uint BATCH_SIZE = 10;
        uint batchDepth = 0;
        List<BlockLocation> blockLocationBatch = new List<BlockLocation>();

        while (batchDepth++ < BATCH_SIZE)
        {
          if (socketProbe.IsPayloadAssigned())
          {
            finalList = true;
            return blockLocationBatch;
          }

          blockLocationBatch.Add(socketProbe.GetBlockLocation());

          if (socketProbe.IsGenesis())
          {
            finalList = true;
            return blockLocationBatch;
          }

          socketProbe.Push();
        }

        finalList = false;
        return blockLocationBatch;
      }


      async Task RenewChannelAsync(BlockchainChannel channel)
      {
        Network.CloseChannel(channel.Buffer);
        Channels.Remove(channel);

        if(Channels.Count < CHANNELS_COUNT)
        {
          BlockchainChannel newChannel = await CreateChannelAsync();
          Task channelStartTask = newChannel.StartAsync();
        }
      }

    }
  }
}