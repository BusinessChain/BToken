﻿using System;
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
      Network Network;
      IPEndPoint IPEndPoint;
      PeerHandshakeManager ConnectionManager;

      TcpClient TcpClient;
      MessageStreamer NetworkMessageStreamer;

      public BufferBlock<NetworkMessage> NetworkMessageBufferUTXO = new BufferBlock<NetworkMessage>();
      public BufferBlock<NetworkMessage> NetworkMessageBufferBlockchain = new BufferBlock<NetworkMessage>();
      
      uint PenaltyScore = 0;

      bool SendHeadersFlag = false;

      // API
      public Peer(IPEndPoint ipEndPoint, Network network)
      {
        Network = network;

        ConnectionManager = new PeerHandshakeManager(this);
        IPEndPoint = ipEndPoint;
      }


      public async Task startAsync(uint blockheightLocal)
      {
        await EstablishPeerConnectionAsync(blockheightLocal);

        Task processMessagesTask = ProcessMessagesAsync();
      }
      async Task EstablishPeerConnectionAsync(uint blockheightLocal)
      {
        try
        {
          var cts = new CancellationTokenSource(TimeSpan.FromSeconds(3));
          CancellationToken token = cts.Token;

          await ConnectTCPAsync();
          await handshakeAsync(blockheightLocal, token);
        }
        catch (Exception ex)
        {
          Dispose();

          throw new NetworkException(string.Format("Connection failed with peer '{0}:{1}'", IPEndPoint.Address.ToString(), IPEndPoint.Port.ToString()), ex);
        }
      }
      async Task ConnectTCPAsync()
      {
        TcpClient = new TcpClient();
        await TcpClient.ConnectAsync(IPEndPoint.Address, IPEndPoint.Port);
        NetworkMessageStreamer = new MessageStreamer(TcpClient.GetStream());
      }
      async Task handshakeAsync(uint blockchainHeightLocal, CancellationToken cancellationToken)
      {
        await NetworkMessageStreamer.WriteAsync(new VersionMessage(blockchainHeightLocal));

        while (!ConnectionManager.isHandshakeCompleted())
        {
          NetworkMessage messageRemote = await NetworkMessageStreamer.ReadAsync();
          await ConnectionManager.ProcessResponseToVersionMessageAsync(messageRemote);
        }
      }
      async Task ProcessMessagesAsync()
      {
        try
        {
          while (true)
          {
            NetworkMessage networkMessage = await NetworkMessageStreamer.ReadAsync();

            switch (networkMessage.Command)
            {
              case "ping":
                await ProcessPingMessageAsync(networkMessage);
                break;
              case "sendheaders":
                await ProcessSendHeadersMessageAsync(networkMessage);
                break;
              case "inv":
                await ProcessInventoryMessageAsync(networkMessage);
                break;
              case "headers":
                await ProcessHeadersMessageAsync(networkMessage);
                break;
              default:
                break;
            }
          }
        }
        catch
        {
          Dispose();
        }
      }
      async Task ProcessPingMessageAsync(NetworkMessage networkMessage)
      {
        PingMessage pingMessage = new PingMessage(networkMessage);
        await NetworkMessageStreamer.WriteAsync(new PongMessage(pingMessage.Nonce));
      }
      async Task ProcessSendHeadersMessageAsync(NetworkMessage networkMessage)
      {
        SendHeadersFlag = true;
        await NetworkMessageStreamer.WriteAsync(new SendHeadersMessage());
      }
      async Task ProcessInventoryMessageAsync(NetworkMessage networkMessage)
      {
        InvMessage invMessage = new InvMessage(networkMessage);

        if (invMessage.GetBlockInventories().Any()) // direkt als property zu kreationszeit anlegen.
        {
          await NetworkMessageBufferBlockchain.SendAsync(invMessage);
        }
        if (invMessage.GetTXInventories().Any())
        {
          await NetworkMessageBufferUTXO.SendAsync(invMessage);
        };
      }
      async Task ProcessHeadersMessageAsync(NetworkMessage networkMessage)
      {
        HeadersMessage headersMessage = new HeadersMessage(networkMessage);
        await NetworkMessageBufferBlockchain.SendAsync(headersMessage);
      }
      public bool IsOwnerOfBuffer(BufferBlock<NetworkMessage> buffer)
      {
        return buffer == NetworkMessageBufferBlockchain || buffer == NetworkMessageBufferUTXO;
      }

      public async Task SendMessageAsync(NetworkMessage networkMessage)
      {
        await NetworkMessageStreamer.WriteAsync(networkMessage);
      }
      
      public async Task GetHeadersAsync(List<UInt256> headerLocator)
      {
        await NetworkMessageStreamer.WriteAsync(new GetHeadersMessage(headerLocator));
      }
    
      public void Blame(uint penaltyScore)
      {
        PenaltyScore += penaltyScore;
      }
          
      public void Dispose()
      {
        TcpClient.Close();

        NetworkMessageBufferBlockchain.Post(null);
        NetworkMessageBufferUTXO.Post(null);

        Network.Peers.Remove(this);
      }
      
      public async Task PingAsync()
      {
        await NetworkMessageStreamer.WriteAsync(new PingMessage(Nonce));
      }
    }
  }
}
