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
      partial class BlockchainSession
      {
        BlockchainController Controller;
        public BufferBlock<NetworkMessage> Buffer;

        public BlockchainSession() { }
        public BlockchainSession(BufferBlock<NetworkMessage> buffer, BlockchainController controller)
        {
          Buffer = buffer;
          Controller = controller;
        }
        
        public async Task StartAsync()
        {
          try
          {
            await InitiateHeaderDownloadAsync();

            while (true)
            {
              NetworkMessage networkMessage = await GetNextNetworkMessageAsync();
              await ProcessMessageAsync(networkMessage);
            }
          }
          catch
          {
            Controller.DisposeSession(this);
          }
        }
        async Task<NetworkMessage> GetNextNetworkMessageAsync()
        {
          NetworkMessage networkMessage = await Buffer.ReceiveAsync();
          if (networkMessage == null)
          {
            throw new NetworkException("Network aborted session.");
          }

          return networkMessage;
        }
               
        virtual protected async Task ProcessMessageAsync(NetworkMessage networkMessage)
        {
          switch (networkMessage)
          {
            case InvMessage invMessage:
              await ProcessInventoryMessageAsync(invMessage);
              break;

            case HeadersMessage headersMessage:
              await new HeadersSession(this).StartAsync(headersMessage);
              break;

            default:
              break;
          }
        }
        async Task ProcessInventoryMessageAsync(InvMessage invMessage)
        {
          foreach (Inventory blockInventory in invMessage.GetBlockInventories())
          {
            ChainBlock chainBlock = Controller.Blockchain.GetBlock(blockInventory.Hash);

            if (chainBlock == null)
            {
              await GetHeadersAsync(GetBlockLocator());
              return;
            }
            else
            {
              BlameProtocolError();
            }
          }
        }
        
        public async Task InitiateHeaderDownloadAsync()
        {
          await GetHeadersAsync(GetBlockLocator());
        }

        UInt256 GetLastBlockHash()
        {
          return Controller.Blockchain.SocketMain.Hash;
        }
        async Task GetHeadersAsync(UInt256 rootBlockHash)
        {
          await GetHeadersAsync(new List<UInt256> { rootBlockHash });
        }
        async Task GetHeadersAsync(List<UInt256> headerLocator)
        {
          await Controller.Network.GetHeadersAsync(Buffer, headerLocator);
        }
        void BlameConsensusError()
        {
          Controller.Network.BlameConsensusError(Buffer);
        }
        void BlameProtocolError()
        {
          Controller.Network.BlameProtocolError(Buffer);
        }
        List<UInt256> GetBlockLocator()
        {
          return Controller.Blockchain.GetBlockLocator();
        }
      }
    }
  }
}
