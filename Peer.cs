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
  partial class NetworkAdapter
  {
    partial class Peer
    {      
      IPEndPoint IPEndPoint;
      TcpClient TCPClient;
      MessageStreamer MessageStreamer;
      CancellationTokenSource cts = new CancellationTokenSource(); // sinnvolle Organisation der cancellation Token finden.

      PeerConnectionManager ConnectionManager;


      // API
      public Peer(IPEndPoint ipEndPoint, NetworkAdapter networkAdapter)
      {
        ConnectionManager = new PeerConnectionManager(this);
        IPEndPoint = ipEndPoint;
        initializeTCPClient();
      }
      void initializeTCPClient()
      {
        TCPClient = new TcpClient(AddressFamily.InterNetworkV6);
        TCPClient.Client.SetSocketOption(SocketOptionLevel.IPv6, SocketOptionName.IPv6Only, false);
        TCPClient.Client.ReceiveBufferSize = 1000 * 5000;
        TCPClient.Client.SendBufferSize = 1000 * 1000;
      }


      public async Task startAsync(uint blockheightLocal)
      {
        try
        {
          await connectTCPClient().ConfigureAwait(false);
          await handshakeAsync(blockheightLocal, cancellationTimeout).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
          disconnect();
          Console.WriteLine(ex.Message);
        }
      }
      async Task connectTCPClient()
      {
        await TCPClient.ConnectAsync(IPEndPoint.Address, IPEndPoint.Port).ConfigureAwait(false);
        MessageStreamer = new MessageStreamer(TCPClient.GetStream(), cts.Token);
      }
      async Task handshakeAsync(uint blockheightLocal, CancellationToken cancellation)
      {
        VersionMessage versionMessageLocal = new VersionMessage(blockheightLocal);
        await MessageStreamer.WriteAsync(versionMessageLocal).ConfigureAwait(false);
        
        while (!ConnectionManager.isHandshakeCompleted())
        {
          NetworkMessage messageRemote = await MessageStreamer.ReadAsync().ConfigureAwait(false);
          await ConnectionManager.receiveResponseToVersionMessageAsync(messageRemote);
        }
      }
             
      public async Task SendMessageAsync(NetworkMessage networkMessage)
      {
        await MessageStreamer.WriteAsync(networkMessage);
      }

      public async Task GetHeadersAsync(IEnumerable<UInt256> headerLocator, BufferBlock<NetworkHeader> networkHeaderBuffer)
      {
        HeadersMessage headersMessageRemote;

        do
        {
          await MessageStreamer.WriteAsync(new GetHeadersMessage(headerLocator)).ConfigureAwait(false);
          headersMessageRemote = await readMessageAsync<HeadersMessage>().ConfigureAwait(false);

          if (headersMessageRemote.attachesToHeaderLocator(headerLocator))
          {
            foreach (NetworkHeader header in headersMessageRemote.NetworkHeaders)
            {
              await networkHeaderBuffer.SendAsync(header);
            }
            byte[] lastHashRemote = Hashing.sha256d(headersMessageRemote.NetworkHeaders.Last().getBytes());
            headerLocator = new List<UInt256>() { new UInt256(lastHashRemote) };
          }
          else
          {
            disconnect(); // danach mit anderem Peer versuchen.
          }
        } while (headersMessageRemote.hasMaxHeaderCount());

        networkHeaderBuffer.Post(null); // signal end of buffer
      }

      public async Task<T> readMessageAsync<T>(CancellationToken cancellationToken = default(CancellationToken)) where T : NetworkMessage
      {
        while(true)
        {
          NetworkMessage networkMessage = await readMessageAsync(cancellationToken).ConfigureAwait(false);

          if(networkMessage is T message)
          {
            return message;
          }
        } 
      }
      
      public async Task<NetworkMessage> readMessageAsync(CancellationToken cancellationToken)
      {
        while (true)
        {
          NetworkMessage networkMessage = await MessageStreamer.ReadAsync().ConfigureAwait(false);
          
          switch (networkMessage.Command)
          {
            case "version":
              return new VersionMessage(networkMessage.Payload);
            case "verack":
              return new VerAckMessage();
            case "reject":
              return new RejectMessage(networkMessage.Payload);
            case "inv":
              return new InvMessage(networkMessage.Payload);
            case "headers":
              return new HeadersMessage(networkMessage.Payload);
            default:
              throw new NotSupportedException(string.Format("Peer '{0}' sent unknown NetworkMessage.", IPEndPoint.Address));
          }
        }
      }

      public uint getChainHeight()
      {
        return ConnectionManager.getChainHeight();
      }

      public void disconnect()
      {
        MessageStreamer.Dispose();
        TCPClient.Dispose();
      }
    }
  }
}
