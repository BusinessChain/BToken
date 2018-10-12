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
  partial class BlockchainController
  {
    Network Network;
    Blockchain Blockchain;

    const int CHANNELS_COUNT = 8;

    BlockPayloadLocator BlockLocator;
    BlockArchiver Archiver;


    public BlockchainController(Network network, Blockchain blockchain)
    {
      Network = network;
      Blockchain = blockchain;
      BlockLocator = new BlockPayloadLocator(blockchain, CHANNELS_COUNT);
      Archiver = new BlockArchiver();
    }

    public async Task StartAsync()
    {
      Task<BlockchainChannel>[] createChannelsTasks = CreateChannels();

      //Archiver.LoadBlockchain(Blockchain);

      BlockchainChannel channelFirst = await await Task.WhenAny(createChannelsTasks);
      await DownloadHeadersAsync(channelFirst);

      BlockchainChannel[] channels = await Task.WhenAll(createChannelsTasks);
      await DownloadBlocksAsync(channels);

      //StartListeningToNetworkAsync();
    }
    Task<BlockchainChannel>[] CreateChannels()
    {
      var channelsTasks = new List<BlockchainChannel>();
      for (int i = 0; i < CHANNELS_COUNT; i++)
      {
        channelsTasks.Add(new BlockchainChannel(this));
      }
      
      return channelsTasks.Select(async channel =>
      {
        await channel.ConnectAsync();
        return channel;
      }).ToArray();
    }

    async Task DownloadHeadersAsync(BlockchainChannel channel)
    {
      await channel.ExecuteSessionAsync(new SessionHeaderDownload(this, Blockchain));
    }

    async Task DownloadBlocksAsync(BlockchainChannel[] channels)
    {
      Task[] downloadBlocksTask = channels.Select(async channel =>
      {
        await channel.ExecuteSessionAsync(new SessionBlockDownload(this, BlockLocator));
      }).ToArray();

      await Task.WhenAll(downloadBlocksTask);
    }

  }
}