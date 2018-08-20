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

      int NumberOfSessionsMax = 8;
      List<BlockchainSession> BlockchainSessions = new List<BlockchainSession>();
      

      public BlockchainController(Network network, Blockchain blockchain)
      {
        Network = network;
        Blockchain = blockchain;

      }

      public async Task StartAsync()
      {
        await CreateSessionsAsync();
      }
      async Task CreateSessionsAsync()
      {
        for (int i = 0; i < NumberOfSessionsMax; i++)
        {
          await CreateSessionAsync();
        }
      }
      async Task CreateSessionAsync()
      {
        BufferBlock<NetworkMessage> buffer = await Network.CreateBlockchainSessionAsync(Blockchain.GetHeight());
        
        var blockchainSession = new BlockchainSession(buffer, this);
        BlockchainSessions.Add(blockchainSession);

        Task blockchainSessionStartTask = blockchainSession.StartAsync();
      }

      void DisposeSession(BlockchainSession session)
      {
        Network.DisposeSession(session.Buffer);
        BlockchainSessions.Remove(session);
      }

    }
  }
}