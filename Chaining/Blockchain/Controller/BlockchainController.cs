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

      const int SESSIONS_COUNT = 2;
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
        // Use concurrent collection instead of list
        List<List<BlockLocation>> blockLocationBatches = CreateBlockLocationBatches();

        var downloadBlocksTasks = new List<Task>();
        foreach(BlockchainSession session in Sessions)
        {
          downloadBlocksTasks.Add(session.DownloadBlocksAsync(blockLocationBatches));
        }
        await Task.WhenAll(downloadBlocksTasks);
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
        bool finalBatch = false;

        while (!finalBatch)
        {
          List<BlockLocation> blockLocationBatch = CreateBlockLocationBatch(socketProbe, out finalBatch);
          blockLocationBatches.Add(blockLocationBatch);
        }
      }
      List<BlockLocation> CreateBlockLocationBatch(ChainSocket.SocketProbe socketProbe, out bool finalList)
      {
        const uint BATCH_SIZE = 10;
        uint batchDepth = 0;
        List<BlockLocation> blockLocationBatch = new List<BlockLocation>();

        while (batchDepth++ < BATCH_SIZE)
        {
          if (socketProbe.IsPayloadAssigned())
          {
            finalList = true;
            return blockLocationBatch;
          }

          blockLocationBatch.Add(socketProbe.GetBlockLocation());

          if (socketProbe.IsGenesis())
          {
            finalList = true;
            return blockLocationBatch;
          }

          socketProbe.Push();
        }

        finalList = false;
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