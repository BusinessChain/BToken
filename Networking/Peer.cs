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
    partial class Peer
    {
      Network NetworkAdapter;
      IPEndPoint IPEndPoint;
      PeerConnectionManager ConnectionManager;

      TcpClient TcpClient;
      MessageStreamer MessageStreamer;

      List<NetworkMessage> MessagesReceivedFromRemotePeer = new List<NetworkMessage>();

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
          TcpClient = new TcpClient(new IPEndPoint(IPAddress.Any, 8333));
          await TcpClient.ConnectAsync(IPEndPoint.Address, IPEndPoint.Port);
          MessageStreamer = new MessageStreamer(TcpClient.GetStream());

          await handshakeAsync(blockheightLocal);

          Task processMessagesUnsolicitedTask = ProcessMessagesUnsolicitedAsync();
        }
        catch (Exception ex)
        {
          TcpClient.Close();

          throw new NetworkProtocolException(string.Format("Connection failed with peer '{0}'", IPEndPoint.Address.ToString()), ex);
        }
      }
      async Task handshakeAsync(uint blockchainHeightLocal)
      {
        await MessageStreamer.WriteAsync(new VersionMessage(blockchainHeightLocal));

        while (!ConnectionManager.isHandshakeCompleted())
        {
          NetworkMessage messageRemote = await MessageStreamer.ReadAsync();
          await ConnectionManager.receiveResponseToVersionMessageAsync(messageRemote);
        }
      }
      async Task ProcessMessagesUnsolicitedAsync()
      {
        while (true)
        {
          NetworkMessage message = await MessageStreamer.ReadAsync();
          
          switch (message.Command)
          {
            case "ping":
              PingMessage pingMessage = new PingMessage(message);
              await MessageStreamer.WriteAsync(new PongMessage(pingMessage.Nonce));
              break;
            case "sendheaders":
              SendHeadersFlag = true;
              await MessageStreamer.WriteAsync(new SendHeadersMessage());
              break;
            case "inv":
              InvMessage invMessage = new InvMessage(message);
              MessagesReceivedFromRemotePeer.Add(invMessage);
              await NetworkAdapter.ProcessMessageUnsolicitedAsync(invMessage);
              break;
            case "headers":
              HeadersMessage headersMessage = new HeadersMessage(message);
              MessagesReceivedFromRemotePeer.Add(headersMessage);
              await NetworkAdapter.ProcessMessageUnsolicitedAsync(headersMessage);
              break;
            default:
              break;
          }
        }
      }

      public bool IsOriginOfNetworkMessage(NetworkMessage networkMessage)
      {
        return MessagesReceivedFromRemotePeer.Contains(networkMessage);
      }

      public async Task SendMessageAsync(NetworkMessage networkMessage)
      {
        await MessageStreamer.WriteAsync(networkMessage);
      }

      public async Task GetHeadersAdvertisedAsync(UInt256 headerHashChainTip)
      {
        await GetHeadersAsync(new List<UInt256>() { headerHashChainTip });
      }
      public async Task GetHeadersAsync(IEnumerable<UInt256> headerLocator)
      {
        await MessageStreamer.WriteAsync(new GetHeadersMessage(headerLocator));

        //NetworkMessage remoteNetworkMessageHeaders = await WaitUntilMessageType("headers");
        //HeadersMessage headerMessageRemote = new HeadersMessage(remoteNetworkMessageHeaders);

        //foreach (NetworkHeader header in headerMessageRemote.NetworkHeaders)
        //{
        //  await networkHeaderBuffer.SendAsync(header);
        //}

        //networkHeaderBuffer.Post(null);
      }
    
      async Task<NetworkMessage> WaitUntilMessageType(string messageCommand)
      {
        NetworkMessage networkMessage = await MessageStreamer.ReadAsync();
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
    }
  }
}
