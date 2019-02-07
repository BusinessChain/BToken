using System.Diagnostics;

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
    partial class Peer : INetworkChannel
    {
      Network Network;

      IPEndPoint IPEndPoint;
      TcpClient TcpClient;
      MessageStreamer NetworkMessageStreamer;

      readonly object IsDispatchedLOCK = new object();
      bool IsDispatched = true;

      BufferBlock<NetworkMessage> InboundRequestMessageBuffer;

      ulong FeeFilterValue;


      public Peer(Network network)
      {
        Network = network;

        InboundRequestMessageBuffer = new BufferBlock<NetworkMessage>(
          new DataflowBlockOptions() {
            BoundedCapacity = 2000 });
    }
      public Peer(TcpClient tcpClient, Network network)
        : this(network)
      {
        TcpClient = tcpClient;
        NetworkMessageStreamer = new MessageStreamer(tcpClient.GetStream());

        IPEndPoint = (IPEndPoint)tcpClient.Client.RemoteEndPoint;
      }
      
      public async Task StartAsync()
      {
        try
        {
          await ConnectAsync();

          IsDispatched = false;

          await ProcessNetworkMessagesAsync();
        }
        catch (Exception ex)
        {
          Console.WriteLine("Peer '{0}' threw exception: \n'{1}'",
            IPEndPoint.Address.ToString(),
            ex.Message);

          throw ex;
        }
        finally
        {
          TcpClient.Close();
        }
      }

      async Task ConnectAsync()
      {
        IPAddress iPAddress = Network.AddressPool.GetRandomNodeAddress();
        IPEndPoint = new IPEndPoint(iPAddress, Port);
        await ConnectTCPAsync();
        await HandshakeAsync();

        Console.WriteLine("Connected with peer '{0}'",
            IPEndPoint.Address.ToString());
      }

      async Task ProcessNetworkMessagesAsync()
      {
        while (true)
        {
          NetworkMessage networkMessage = await NetworkMessageStreamer.ReadAsync(default(CancellationToken)).ConfigureAwait(false);

          switch (networkMessage.Command)
          {
            case "version":
              await ProcessVersionMessageAsync(networkMessage, default(CancellationToken)).ConfigureAwait(false);
              break;
            case "ping":
              Task processPingMessageTask = ProcessPingMessageAsync(networkMessage);
              break;
            case "addr":
              ProcessAddressMessage(networkMessage);
              break;
            case "sendheaders":
              Task processSendHeadersMessageTask = ProcessSendHeadersMessageAsync(networkMessage);
              break;
            case "feefilter":
              ProcessFeeFilterMessage(networkMessage);
              break;
            default:
              InboundRequestMessageBuffer.Post(networkMessage);
              Network.PeerRequestInboundBuffer.Post(this);
              break;
          }
        }
      }

      public bool TryDispatch()
      {
        lock (IsDispatchedLOCK)
        {
          if (IsDispatched)
          {
            return false;
          }

          IsDispatched = true;
          return true;
        }
      }
      public void Dispose()
      {
        lock (IsDispatchedLOCK)
        {
          IsDispatched = false;
        }
      }

      public List<NetworkMessage> GetInboundRequestMessages()
      {
        InboundRequestMessageBuffer.TryReceiveAll(out IList<NetworkMessage> requestMessages);
        return (List<NetworkMessage>)requestMessages;
      }

      async Task ConnectTCPAsync()
      {
        TcpClient = new TcpClient();
        await TcpClient.ConnectAsync(IPEndPoint.Address, IPEndPoint.Port);
        NetworkMessageStreamer = new MessageStreamer(TcpClient.GetStream());
      }
      async Task HandshakeAsync()
      {
        await NetworkMessageStreamer.WriteAsync(new VersionMessage());
        
        CancellationToken cancellationToken = new CancellationTokenSource(TimeSpan.FromSeconds(2)).Token;

        var handshakeManager = new PeerHandshakeManager(this);
        while (!handshakeManager.isHandshakeCompleted())
        {
          NetworkMessage messageRemote = await NetworkMessageStreamer.ReadAsync(cancellationToken);
          await handshakeManager.ProcessResponseToVersionMessageAsync(messageRemote);
        }
      }
      async Task ProcessVersionMessageAsync(NetworkMessage networkMessage, CancellationToken cancellationToken)
      {
      }
      async Task ProcessPingMessageAsync(NetworkMessage networkMessage)
      {
        PingMessage pingMessage = new PingMessage(networkMessage);
        await NetworkMessageStreamer.WriteAsync(new PongMessage(pingMessage.Nonce)).ConfigureAwait(false);
      }
      void ProcessFeeFilterMessage(NetworkMessage networkMessage)
      {
        FeeFilterMessage feeFilterMessage = new FeeFilterMessage(networkMessage);
        FeeFilterValue = feeFilterMessage.FeeFilterValue;
      }
      void ProcessAddressMessage(NetworkMessage networkMessage)
      {
        AddressMessage addressMessage = new AddressMessage(networkMessage);
      }
      async Task ProcessSendHeadersMessageAsync(NetworkMessage networkMessage) => await NetworkMessageStreamer.WriteAsync(new SendHeadersMessage()).ConfigureAwait(false);
            
      public async Task SendMessageAsync(NetworkMessage networkMessage) => await NetworkMessageStreamer.WriteAsync(networkMessage).ConfigureAwait(false);
      public async Task<NetworkMessage> ReceiveMessageAsync(CancellationToken cancellationToken) => await InboundRequestMessageBuffer.ReceiveAsync(cancellationToken);
      
      public async Task PingAsync() => await NetworkMessageStreamer.WriteAsync(new PingMessage(Nonce));

      public async Task<bool> TryExecuteSessionAsync(INetworkSession session, CancellationToken cancellationToken)
      {
        try
        {
          await session.RunAsync(this, cancellationToken);
          
          return true;
        }
        catch (Exception ex)
        {
          Console.WriteLine("Session '{0}' with peer '{1}' ended with exception: \n'{2}'",
            session.GetType().ToString(),
            IPEndPoint.Address.ToString(),
            ex.Message);

          return false;
        }
      }

      public uint GetProtocolVersion()
      {
        return ProtocolVersion;
      }
      public string GetIdentification()
      {
        return IPEndPoint.Address.ToString();
      }
    }
  }
}
