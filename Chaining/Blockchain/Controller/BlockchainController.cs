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

      int NumberOfSessionsMax = 1;
      List<BlockchainSession> BlockchainSessions = new List<BlockchainSession>();
      

      public BlockchainController(Network network, Blockchain blockchain)
      {
        Network = network;
        Blockchain = blockchain;

      }

      public async Task StartAsync()
      {
        await CreateBlockchainSessionsAsync();
      }
      async Task CreateBlockchainSessionsAsync()
      {
        for (int i = 0; i < NumberOfSessionsMax; i++)
        {
          BlockchainSession blockchainSession = await CreateBlockchainSessionAsync();
          BlockchainSessions.Add(blockchainSession);
          Task blockchainSessionStartTask = blockchainSession.StartAsync();
        }
      }
      async Task<BlockchainSession> CreateBlockchainSessionAsync()
      {
        BufferBlock<NetworkMessage> buffer = await Network.CreateNetworkSessionBlockchainAsync(Blockchain.GetHeight());
        return new BlockchainSession(buffer, this);
      }

    }
  }
}