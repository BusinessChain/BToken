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

      const int SESSIONS_COUNT = 1;
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

      async Task RequestBlockDownloadAsync()
      {
        List<List<BlockLocation>> blockLocationBatches = CreateBlockLocationBatches();
        //await new BlockSession(Sessions.First()).StartAsync(blockLocationBatches.First());
        await Sessions.First().RequestBlockDownloadAsync(blockLocationBatches.First());
      }
      List<List<BlockLocation>> CreateBlockLocationBatches()
      {
        var blockLocationBatches = new List<List<BlockLocation>>();
        ChainSocket socket = Blockchain.SocketMain;

        while (socket != null)
        {
          socket.Reset();

          CreateBlockLocationBatchesPerSocket(socket.Probe, blockLocationBatches);

          socket = socket.WeakerSocket;
        }

        return blockLocationBatches;
      }
      void CreateBlockLocationBatchesPerSocket(ChainSocket.SocketProbe socketProbe, List<List<BlockLocation>> blockLocationBatches)
      {
        List<BlockLocation> blockLocationBatch = CreateBlockLocationBatch(socketProbe);

        if (blockLocationBatch.Any())
        {
          blockLocationBatches.Add(blockLocationBatch);
          CreateBlockLocationBatchesPerSocket(socketProbe, blockLocationBatches);
        }
      }
      List<BlockLocation> CreateBlockLocationBatch(ChainSocket.SocketProbe socketProbe)
      {
        const uint BATCH_SIZE = 3;
        uint batchDepth = 0;
        List<BlockLocation> blockLocationBatch = new List<BlockLocation>();

        do
        {
          if (socketProbe.IsGenesis())
          {
            return blockLocationBatch;
          }

          socketProbe.Push();

          if (socketProbe.IsPayloadAssigned())
          {
            return blockLocationBatch;
          }

          blockLocationBatch.Add(socketProbe.GetBlockLocation());

        } while (++batchDepth < BATCH_SIZE);

        return blockLocationBatch;
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