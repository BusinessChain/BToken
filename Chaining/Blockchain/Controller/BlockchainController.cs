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

  public partial class Blockchain
  {
    partial class BlockchainController
    {
      Network Network;
      Blockchain Blockchain;

      const int CHANNELS_COUNT = 8;
      List<BlockchainChannel> Channels = new List<BlockchainChannel>();
      
      Archiver Archiver;


      public BlockchainController(Network network, Blockchain blockchain)
      {
        Network = network;
        Blockchain = blockchain;

        for (int i = 0; i < CHANNELS_COUNT; i++)
        {
          Channels.Add(new BlockchainChannel(this));
        }

        Archiver = new Archiver(blockchain);
      }

      public async Task StartAsync()
      {
        Task<BlockchainChannel>[] connectChannelsTasks = ConnectChannelsAsync();

        LoadHeadersFromArchive();
        
        BlockchainChannel channelConnectedFirst = await await Task.WhenAny(connectChannelsTasks);
        await DownloadHeadersAsync(channelConnectedFirst);

        StartListeners(connectChannelsTasks);
      }
      Task<BlockchainChannel>[] ConnectChannelsAsync()
      {
        return Channels.Select(async channel =>
        {
          await channel.ConnectAsync();
          return channel;
        }).ToArray();

      }
      void StartListeners(Task<BlockchainChannel>[] createChannelsTasks)
      {
        createChannelsTasks.Select(async createChannelsTask =>
        {
          BlockchainChannel channel = await createChannelsTask;
          await channel.StartMessageListenerAsync();
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
              UInt256 headerHash = new UInt256(Hashing.SHA256d(header.GetBytes()));
              Blockchain.InsertHeader(header, headerHash);

              header = archiveReader.GetNextHeader();
            }
          }
        }
        catch (Exception ex)
        {
          Debug.WriteLine(ex.Message);
        }
      }

      async Task DownloadHeadersAsync(BlockchainChannel channel)
      {
        await channel.ExecuteSessionAsync(new SessionHeaderDownload(this));
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