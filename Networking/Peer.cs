using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using System.Threading.Tasks.Dataflow;

namespace BToken.Networking
{
  partial class Network
  {
    partial class Peer : IDisposable
    {
      Network NetworkAdapter;
      IPEndPoint IPEndPoint;
      PeerConnectionManager ConnectionManager;

      TcpClient TcpClient;
      MessageStreamer NetworkMessageStreamer;

      //Bound the capacity of that List
      List<NetworkMessage> MessagesReceivedFromRemotePeer = new List<NetworkMessage>();

      uint PenaltyScore = 0;

      bool OrphanParentHeaderRequestPending = false;
      bool SendHeadersFlag = false;

      // API
      public Peer(IPEndPoint ipEndPoint, Network networkAdapter)
      {
        NetworkAdapter = networkAdapter;

        ConnectionManager = new PeerConnectionManager(this);
        IPEndPoint = ipEndPoint;
      }


      public async Task startAsync(uint blockheightLocal)
      {
        try
        {
          await EstablishTcpConnection();

          await handshakeAsync(blockheightLocal);

          Task processMessagesIncomingTask = ProcessMessagesIncomingAsync();
        }
        catch (Exception ex)
        {
          Dispose();

          throw new NetworkException(string.Format("Connection failed with peer '{0}:{1}'", IPEndPoint.Address.ToString(), IPEndPoint.Port.ToString()), ex);
        }
      }
      async Task EstablishTcpConnection()
      {
        TcpClient = new TcpClient(new IPEndPoint(IPAddress.Any, 8333));
        await TcpClient.ConnectAsync(IPEndPoint.Address, IPEndPoint.Port);
        NetworkMessageStreamer = new MessageStreamer(TcpClient.GetStream());
      }
      async Task handshakeAsync(uint blockchainHeightLocal)
      {
        await NetworkMessageStreamer.WriteAsync(new VersionMessage(blockchainHeightLocal));

        while (!ConnectionManager.isHandshakeCompleted())
        {
          NetworkMessage messageRemote = await NetworkMessageStreamer.ReadAsync();
          await ConnectionManager.receiveResponseToVersionMessageAsync(messageRemote);
        }
      }
      async Task ProcessMessagesIncomingAsync()
      {
        while (true)
        {
          NetworkMessage networkMessage = await NetworkMessageStreamer.ReadAsync();
          
          switch (networkMessage.Command)
          {
            case "ping":
              await processPingMessage(networkMessage);
              break;
            case "sendheaders":
              await processSendHeadersMessage(networkMessage);
              break;
            case "inv":
              await processInventoryMessage(networkMessage);
              break;
            case "headers":
              await processHeadersMessage(networkMessage);
              break;
            default:
              break;
          }
        }
      }
      async Task processPingMessage(NetworkMessage networkMessage)
      {
        PingMessage pingMessage = new PingMessage(networkMessage);
        await NetworkMessageStreamer.WriteAsync(new PongMessage(pingMessage.Nonce));
      }
      async Task processSendHeadersMessage(NetworkMessage networkMessage)
      {
        SendHeadersFlag = true;
        await NetworkMessageStreamer.WriteAsync(new SendHeadersMessage());
      }
      async Task processInventoryMessage(NetworkMessage networkMessage)
      {
        InvMessage invMessage = new InvMessage(networkMessage);
        MessagesReceivedFromRemotePeer.Add(invMessage);
        await NetworkAdapter.RelayMessageIncomingAsync(invMessage);
      }
      async Task processHeadersMessage(NetworkMessage networkMessage)
      {
        HeadersMessage headersMessage = new HeadersMessage(networkMessage);
        MessagesReceivedFromRemotePeer.Add(headersMessage);
        await NetworkAdapter.RelayMessageIncomingAsync(headersMessage);
      }

      public bool IsOriginOfNetworkMessage(NetworkMessage networkMessage)
      {
        return MessagesReceivedFromRemotePeer.Contains(networkMessage);
      }

      public async Task SendMessageAsync(NetworkMessage networkMessage)
      {
        await NetworkMessageStreamer.WriteAsync(networkMessage);
      }

      public async Task RequestOrphanParentHeadersAsync(List<UInt256> headerLocator)
      {
        await GetHeadersAsync(headerLocator);
        OrphanParentHeaderRequestPending = true;
      }
      public async Task GetHeadersAsync(IEnumerable<UInt256> headerLocator)
      {
        await NetworkMessageStreamer.WriteAsync(new GetHeadersMessage(headerLocator));
      }
    
      public void Blame(uint penaltyScore)
      {
        PenaltyScore += penaltyScore;
      }

      async Task<NetworkMessage> WaitUntilMessageType(string messageCommand)
      {
        NetworkMessage networkMessage = await NetworkMessageStreamer.ReadAsync();
        if (networkMessage.Command == messageCommand)
        {
          return networkMessage;
        }

        return await WaitUntilMessageType(messageCommand);
      }
      
      public uint getChainHeight()
      {
        return ConnectionManager.getChainHeight();
      }

      public void Dispose()
      {
        TcpClient.Close();
      }
    }
  }
}
