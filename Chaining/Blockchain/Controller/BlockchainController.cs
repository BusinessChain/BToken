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

      const int SESSIONS_COUNT = 8;
      List<BlockchainSession> Sessions = new List<BlockchainSession>();
      

      public BlockchainController(Network network, Blockchain blockchain)
      {
        Network = network;
        Blockchain = blockchain;

      }

      public async Task StartAsync()
      {
        for(int i = 0; i < SESSIONS_COUNT; i++)
        {
          Task createSessionTask = CreateSessionAsync();
        }
        
      }
      async Task CreateSessionAsync()
      {
        BufferBlock<NetworkMessage> buffer = await Network.CreateBlockchainSessionAsync(Blockchain.GetHeight());
        
        var blockchainSession = new BlockchainSession(buffer, this);
        Sessions.Add(blockchainSession);

        Task sessionStartTask = blockchainSession.StartAsync();
      }

      void DisposeSession(BlockchainSession session)
      {
        Network.DisposeSession(session.Buffer);
        Sessions.Remove(session);

        if(Sessions.Count < SESSIONS_COUNT)
        {
          Task createSessionTask = CreateSessionAsync();
        }
      }

    }
  }
}