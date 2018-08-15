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

      List<NetworkMessageSession> NetworkMessageSessions = new List<NetworkMessageSession>();



      public BlockchainController(Network network, Blockchain blockchain)
      {
        Network = network;
        Blockchain = blockchain;
      }

      public async Task StartAsync()
      {
        await ProcessNetworkSessionsAsync();
      }
      async Task ProcessNetworkSessionsAsync()
      {
        while (true)
        {
          List<NetworkMessageSession> sessions = await GetNetworkMessageSessionsAsync();

          var sessionsProcessingNextMessageTasks = new List<Task>();
          foreach (NetworkMessageSession session in sessions)
          {
            Task sessionProcessingNextMessageTask = session.ProcessNextMessageAsync();
            sessionsProcessingNextMessageTasks.Add(sessionProcessingNextMessageTask);
          }

          Task firstSessionProcessingNextMessageTask = await Task.WhenAny(sessionsProcessingNextMessageTasks);
        }
      }
      async Task<List<NetworkMessageSession>> GetNetworkMessageSessionsAsync()
      {
        List<BufferBlock<NetworkMessage>> buffers = await Network.GetNetworkBuffersBlockchainAsync();

        var sessions = new List<NetworkMessageSession>();
        foreach (BufferBlock<NetworkMessage> buffer in buffers)
        {
          NetworkMessageSession session = NetworkMessageSessions.Find(s => s.ContainsBuffer(buffer));

          if (session == null)
          {
            session = new NetworkMessageSession(buffer, this);
          }

          sessions.Add(session);
        }

        NetworkMessageSessions = sessions;

        return NetworkMessageSessions;
      }

    }
  }
}