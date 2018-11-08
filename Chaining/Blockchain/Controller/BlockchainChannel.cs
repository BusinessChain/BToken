using System.Diagnostics;

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
      partial class BlockchainChannel
      {
        Blockchain Blockchain;
        Network Network;
        IHeaderArchiver Archiver;

        public BufferBlock<NetworkMessage> Buffer;


        public BlockchainChannel(Blockchain blockchain, INetwork network, IHeaderArchiver archiver)
        {
          Blockchain = blockchain;
          Network = (Network)network;
          Archiver = archiver;
        }

        public async Task StartMessageListenerAsync()
        {
          while (true)
          {
            await ProcessNextMessageAsync();
          }
        }

        public async Task ExecuteSessionAsync(BlockchainSession session)
        {
          int sessionExcecutionTries = 0;

          while (true)
          {
            try
            {
              if (Buffer == null)
              {
                await ConnectAsync();
              }

              await session.StartAsync(this);
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
          uint blockchainHeight = Blockchain.MainChain.Height;
          Buffer = await Network.CreateBlockchainChannelAsync(blockchainHeight);
        }
        public async Task ConnectInboundAsync()
        {
          uint blockchainHeight = Blockchain.MainChain.Height;
          Buffer = await Network.AcceptInboundBlockchainChannelAsync(blockchainHeight);
        }

        void Disconnect()
        {
          Network.CloseChannel(Buffer);
          Buffer = null;
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
              ProcessHeadersMessage(headersMessage);
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
          //foreach (Inventory blockInventory in invMessage.GetBlockInventories())
          //{

          //  ChainBlock chainBlock = Controller.Blockchain.GetBlock(blockInventory.Hash);

          //  if (chainBlock == null)
          //  {
          //    await RequestHeadersAsync(GetHeaderLocator());
          //    return;
          //  }
          //  else
          //  {
          //    BlameProtocolError();
          //  }
          //}
        }

        void ProcessHeadersMessage(Network.HeadersMessage headersMessage)
        {
          foreach (NetworkHeader header in headersMessage.Headers)
          {
            try
            {
              Blockchain.InsertHeader(header);
            }
            catch (BlockchainException ex)
            {
              switch (ex.ErrorCode)
              {
                case BlockCode.ORPHAN:
                  //await ProcessOrphanSessionAsync(headerHash);
                  return;

                case BlockCode.DUPLICATE:
                  return;

                default:
                  throw ex;
              }
            }

            using (var archiveWriter = Archiver.GetWriter())
            {
              archiveWriter.StoreHeader(header);
            }
          }
        }

        public async Task RequestHeadersAsync(List<BlockLocation> headerLocator) => await Network.GetHeadersAsync(Buffer, headerLocator.Select(b => b.Hash).ToList());

        public async Task RequestBlocksAsync(List<UInt256> blockHashes) => await Network.GetBlockAsync(Buffer, blockHashes).ConfigureAwait(false);

        void BlameConsensusError()
        {
          Network.BlameConsensusError(Buffer);
        }
        void BlameProtocolError()
        {
          Network.BlameProtocolError(Buffer);
        }
      }
    }
  }
}
