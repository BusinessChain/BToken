﻿using System.Diagnostics;

using System;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Threading.Tasks.Dataflow;

using BToken.Networking;

namespace BToken.Chaining
{

  public partial class Blockchain
  {
    partial class BlockchainController
    {
      Network Network;
      Blockchain Blockchain;

      const int CHANNELS_COUNT_OUTBOUND = 8;
      List<BlockchainChannel> ChannelsOutbound = new List<BlockchainChannel>();

      List<BlockchainChannel> ChannelsInbound = new List<BlockchainChannel>();

      Archiver Archiver;


      public BlockchainController(Network network, Blockchain blockchain)
      {
        Network = network;
        Blockchain = blockchain;

        for (int i = 0; i < CHANNELS_COUNT_OUTBOUND; i++)
        {
          ChannelsOutbound.Add(new BlockchainChannel(this));
        }

        Archiver = new Archiver(blockchain);
      }

      public async Task StartAsync()
      {
        Task<BlockchainChannel>[] connectChannelsTasks = ConnectChannelsAsync();

        LoadHeadersFromArchive();
        
        BlockchainChannel channelConnectedFirst = await await Task.WhenAny(connectChannelsTasks);
        await channelConnectedFirst.ExecuteSessionAsync(new SessionHeaderDownload(this));

        StartMessageListeners(connectChannelsTasks);

        Task inboundChannelListenerTask = StartInboundChannelListenerAsync();
      }
      async Task StartInboundChannelListenerAsync()
      {
        while(ChannelsInbound.Count <= Network.PEERS_COUNT_INBOUND)
        {
          var channelInbound = new BlockchainChannel(this);
          await channelInbound.ConnectInboundAsync();
          ChannelsInbound.Add(channelInbound);
        }
      }
      Task<BlockchainChannel>[] ConnectChannelsAsync()
      {
        return ChannelsOutbound.Select(async channel =>
        {
          await channel.ConnectAsync();
          return channel;
        }).ToArray();
      }
      void StartMessageListeners(Task<BlockchainChannel>[] createChannelsTasks)
      {
        createChannelsTasks.Select(async createChannelsTask =>
        {
          BlockchainChannel channel = await createChannelsTask;
          Task listenerTask = channel.StartMessageListenerAsync();
        }).ToArray();
      }

      void LoadHeadersFromArchive()
      {
        try
        {
          using (var archiveReader = new HeaderArchiver.HeaderReader())
          {
            NetworkHeader header = archiveReader.GetNextHeader();

            while (header != null)
            {
              Blockchain.InsertHeader(header);

              header = archiveReader.GetNextHeader();
            }
          }
        }
        catch (Exception ex)
        {
          Debug.WriteLine(ex.Message);
        }
      }
      
      async Task DownloadBlocksAsync(BlockchainChannel[] channels)
      {
        Task[] downloadBlocksTask = channels.Select(async channel =>
        {
          await channel.ExecuteSessionAsync(new SessionBlockDownload(this));
        }).ToArray();

        await Task.WhenAll(downloadBlocksTask);
      }

    }
  }
}