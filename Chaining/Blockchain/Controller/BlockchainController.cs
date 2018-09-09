﻿using System.Diagnostics;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
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

      const int CHANNELS_COUNT = 8;
      List<BlockchainChannel> Channels = new List<BlockchainChannel>();
      

      public BlockchainController(Network network, Blockchain blockchain)
      {
        Network = network;
        Blockchain = blockchain;
      }

      public async Task StartAsync()
      {
        await DownloadHeadersAsync();

        await DownloadBlocksAsync();

        //StartListeningToNetworkAsync();
      }

      async Task DownloadHeadersAsync()
      {
        await CreateChannelAsync();
        await Channels.First().ExecuteSessionAsync(new SessionHeaderDownload(Blockchain));
      }
      async Task DownloadBlocksAsync()
      {
        List<List<BlockLocation>> blockLocationBatches = CreateBlockLocationBatches();

        var createChannelTasks = new List<Task<BlockchainChannel>>();
        for (int i = 0; i < CHANNELS_COUNT; i++)
        {
          createChannelTasks.Add(CreateChannelAsync());
        }

        Task[] downloadBlocksTask = createChannelTasks.Select(async c =>
        {
          BlockchainChannel channel = await c;

          while (blockLocationBatches.Any())
          {
            List<BlockLocation> blockLocations = blockLocationBatches[0];
            blockLocationBatches.RemoveAt(0);
            Debug.WriteLine("Channel " + channel.GetHashCode() + " adds block " + Thread.CurrentThread.ManagedThreadId);
            await channel.ExecuteSessionAsync(new SessionBlockDownload(Blockchain, blockLocations));
          }
        }).ToArray();

        await Task.WhenAll(downloadBlocksTask);
        Debug.WriteLine("All channels completed session");
      }


      void CreateChannels(int count)
      {
        var createChannelTasks = new List<Task<BlockchainChannel>>();

        for (int i = 0; i < count - 1; i++)
        {
          createChannelTasks.Add(CreateChannelAsync());
        }

        //createChannelTasks.Select(async c =>
        //{
        //  BlockchainChannel channel = await c;
        //  Task startMessageListenerTask = channel.StartMessageListenerAsync();
        //}).ToArray();
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
        const uint BATCH_SIZE = 5;
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


      async Task<BlockchainChannel> RenewChannelAsync(BlockchainChannel channel)
      {
        CloseChannel(channel);

        BlockchainChannel newChannel = await CreateChannelAsync();
        Channels.Add(newChannel);
        return newChannel;
      }
      void CloseChannel(BlockchainChannel channel)
      {
        Network.CloseChannel(channel.Buffer);
        Channels.Remove(channel);
      }

    }
  }
}