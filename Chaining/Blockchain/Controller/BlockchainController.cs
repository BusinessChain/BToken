using System.Diagnostics;

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
    IBlockParser BlockParser;

    const int CHANNELS_COUNT = 8;
    List<BlockchainChannel> Channels = new List<BlockchainChannel>();

    BlockPayloadLocator BlockLocator;
    BlockArchiver Archiver;


    public BlockchainController(Network network, Blockchain blockchain, IBlockParser blockParser)
    {
      Network = network;
      Blockchain = blockchain;
      BlockParser = blockParser;
      BlockLocator = new BlockPayloadLocator(blockchain, CHANNELS_COUNT);
      Archiver = new BlockArchiver();
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
      var createChannelTasks = new List<Task<BlockchainChannel>>();
      for (int i = 0; i < CHANNELS_COUNT; i++)
      {
        createChannelTasks.Add(CreateChannelAsync());
      }

      Task[] downloadBlocksTask = createChannelTasks.Select(async c =>
      {
        BlockchainChannel channel = await c;
        await channel.ExecuteSessionAsync(new SessionBlockDownload(this, BlockLocator));
      }).ToArray();

      await Task.WhenAll(downloadBlocksTask);
    }

    async Task<BlockchainChannel> CreateChannelAsync()
    {
      BufferBlock<NetworkMessage> buffer = await Network.CreateBlockchainChannelAsync(Blockchain.GetHeight()).ConfigureAwait(false);
      BlockchainChannel channel = new BlockchainChannel(buffer, this);
      Channels.Add(channel);
      return channel;
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