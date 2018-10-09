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
    partial class BlockchainChannel
    {
      BlockchainController Controller;
      public BufferBlock<NetworkMessage> Buffer;


      public BlockchainChannel() { }
      public BlockchainChannel(BlockchainController controller)
      {
        Controller = controller;
      }

      //public async Task StartMessageListenerAsync()
      //{
      //  try
      //  {
      //    while (true)
      //    {
      //      await ProcessNextMessageAsync();
      //    }
      //  }
      //  catch (Exception ex)
      //  {
      //    BlockchainChannel channel = await Controller.RenewChannelAsync(this);
      //    Task startChannelTask = channel.StartMessageListenerAsync();
      //  }
      //}

      public async Task ExecuteSessionAsync(BlockchainSession session)
      {
        int sessionExcecutionTries = 0;

        while (true)
        {
          try
          {
            if(!IsConnected())
            {
              await ConnectAsync().ConfigureAwait(false);
            }

            await session.StartAsync(this).ConfigureAwait(false);
            return;
          }
          catch (Exception ex)
          {
            Debug.WriteLine("BlockchainChannel::ExcecuteChannelSession:" + ex.Message +
            ", Session excecution tries: '{0}'", ++sessionExcecutionTries);

            Disconnect();
          }
        }
      }

      public async Task ConnectAsync()
      {
        uint blockchainHeight = Controller.Blockchain.GetHeight();
        Buffer = await Controller.Network.CreateBlockchainChannelAsync(blockchainHeight).ConfigureAwait(false);
      }

      void Disconnect()
      {
        Controller.Network.CloseChannel(Buffer);
        Buffer = null;
      }

      bool IsConnected()
      {
        return Buffer != null;
      }

      async Task ProcessNextMessageAsync()
      {
        NetworkMessage networkMessage = await GetNetworkMessageAsync(default(CancellationToken));

        switch (networkMessage)
        {
          case InvMessage invMessage:
            //await ProcessInventoryMessageAsync(invMessage);
            break;

          case Network.HeadersMessage headersMessage:
            //await ExecuteSessionAsync(new SessionHeaderDownload(headersMessage));
            break;

          case Network.BlockMessage blockMessage:
            break;

          default:
            break;
        }
      }

      public async Task<NetworkMessage> GetNetworkMessageAsync(CancellationToken cancellationToken)
      {
        NetworkMessage networkMessage = await Buffer.ReceiveAsync(cancellationToken).ConfigureAwait(false);

        return networkMessage 
          ?? throw new NetworkException("Network closed channel.");
      }

      async Task ProcessInventoryMessageAsync(InvMessage invMessage)
      {
        foreach (Inventory blockInventory in invMessage.GetBlockInventories())
        {

          ChainBlock chainBlock = Controller.Blockchain.GetBlock(blockInventory.Hash);

          if (chainBlock == null)
          {
            await RequestHeadersAsync(GetHeaderLocator());
            return;
          }
          else
          {
            BlameProtocolError();
          }
        }
      }

      public async Task RequestHeadersAsync(List<BlockLocation> headerLocator) => await Controller.Network.GetHeadersAsync(Buffer, headerLocator.Select(b => b.Hash).ToList());
      List<BlockLocation> GetHeaderLocator() => Controller.Blockchain.GetBlockLocations();

      public async Task RequestBlocksAsync(List<UInt256> blockHashes) => await Controller.Network.GetBlockAsync(Buffer, blockHashes).ConfigureAwait(false);

      void BlameConsensusError()
      {
        Controller.Network.BlameConsensusError(Buffer);
      }
      void BlameProtocolError()
      {
        Controller.Network.BlameProtocolError(Buffer);
      }
    }
  }
}
