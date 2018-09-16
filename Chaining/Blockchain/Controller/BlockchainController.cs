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

    const int CHANNELS_COUNT = 8;
    List<BlockchainChannel> Channels = new List<BlockchainChannel>();

    BlockPayloadLocator BlockLocator;
    IBlockPayloadParser BlockPayloadParser;


    public BlockchainController(Network network, Blockchain blockchain, IBlockPayloadParser blockPayloadParser)
    {
      Network = network;
      Blockchain = blockchain;
      BlockLocator = new BlockPayloadLocator(blockchain);
      BlockPayloadParser = blockPayloadParser;
    }

    public async Task StartAsync()
    {
      await DownloadHeadersAsync();

      await DownloadPayloadsAsync();

      //StartListeningToNetworkAsync();
    }

    async Task DownloadHeadersAsync()
    {
      await CreateChannelAsync();
      await Channels.First().ExecuteSessionAsync(new SessionHeaderDownload(Blockchain));
    }
    async Task DownloadPayloadsAsync()
    {
      var createChannelTasks = new List<Task<BlockchainChannel>>();
      for (int i = 0; i < CHANNELS_COUNT; i++)
      {
        createChannelTasks.Add(CreateChannelAsync());
      }

      Task[] downloadBlocksTask = createChannelTasks.Select(async c =>
      {
        BlockchainChannel channel = await c;
        await channel.ExecuteSessionAsync(new SessionBlockDownload(Blockchain, BlockLocator, BlockPayloadParser));
      }).ToArray();

      await Task.WhenAll(downloadBlocksTask);
      Debug.WriteLine("All channels completed session");
    }

    async Task<BlockchainChannel> CreateChannelAsync()
    {
      BufferBlock<NetworkMessage> buffer = await Network.CreateBlockchainChannelAsync(Blockchain.GetHeight());
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