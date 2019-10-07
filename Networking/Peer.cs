using System.Diagnostics;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using System.Threading.Tasks.Dataflow;
using System.Security.Cryptography;

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

      BufferBlock<NetworkMessage> ApplicationMessages = 
        new BufferBlock<NetworkMessage>();

      ulong FeeFilterValue;


      public Peer(Network network)
      {
        Network = network;
      }
      public Peer(TcpClient tcpClient, Network network)
        : this(network)
      {
        TcpClient = tcpClient;
        NetworkMessageStreamer = new MessageStreamer(tcpClient.GetStream());

        IPEndPoint = (IPEndPoint)tcpClient.Client.RemoteEndPoint;
      }

      public async Task Start()
      {
        try
        {
          await Connect();

          lock (IsDispatchedLOCK)
          {
            IsDispatched = false;
          }

          await ProcessNetworkMessagesAsync();
        }
        catch (Exception ex)
        {
          throw ex;
        }
        finally
        {
          TcpClient.Close();
        }
      }

      public async Task<bool> TryConnect()
      {
        try
        {
          await Connect();

          ProcessNetworkMessagesAsync();

          return true;
        }
        catch
        {
          return false;
        }
      }


      public async Task Connect()
      {
        IPAddress iPAddress = Network.AddressPool.GetRandomNodeAddress();
        IPEndPoint = new IPEndPoint(iPAddress, Port);
        await ConnectTCPAsync();
        await HandshakeAsync();
      }

      public async Task ProcessNetworkMessagesAsync()
      {
        while (true)
        {
          NetworkMessage message = await NetworkMessageStreamer
            .ReadAsync(default).ConfigureAwait(false);

          switch (message.Command)
          {
            case "version":
              await ProcessVersionMessageAsync(message, default).ConfigureAwait(false);
              break;
            case "ping":
              Task processPingMessageTask = ProcessPingMessageAsync(message);
              break;
            case "addr":
              ProcessAddressMessage(message);
              break;
            case "sendheaders":
              Task processSendHeadersMessageTask = ProcessSendHeadersMessageAsync(message);
              break;
            case "feefilter":
              ProcessFeeFilterMessage(message);
              break;
            default:
              ProcessApplicationMessage(message);
              break;
          }
        }
      }
      void ProcessApplicationMessage(NetworkMessage message)
      {
        ApplicationMessages.Post(message);

        lock (Network.LOCK_ChannelsOutbound)
        {
          if (Network.ChannelsOutboundAvailable.Contains(this))
          {
            Network.ChannelsOutboundAvailable.Remove(this);
            Network.PeersRequestInbound.Post(this);
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
      public void Release()
      {
        lock (IsDispatchedLOCK)
        {
          IsDispatched = false;
        }
      }

      public void Dispose()
      {
        TcpClient.Dispose();
      }

      public List<NetworkMessage> GetApplicationMessages()
      {
        if(ApplicationMessages.TryReceiveAll(out IList<NetworkMessage> messages))
        {
          return (List<NetworkMessage>)messages;
        }

        return new List<NetworkMessage>();
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
      async Task ProcessSendHeadersMessageAsync(NetworkMessage networkMessage) => await NetworkMessageStreamer.WriteAsync(new SendHeadersMessage());

      public async Task SendMessageAsync(NetworkMessage networkMessage)
      {
        await NetworkMessageStreamer.WriteAsync(networkMessage);
      }

      public async Task<NetworkMessage> ReceiveSessionMessageAsync(CancellationToken cancellationToken) 
        => await ApplicationMessages.ReceiveAsync(cancellationToken);
      
      public async Task PingAsync() => await NetworkMessageStreamer.WriteAsync(new PingMessage(Nonce));

      public async Task<byte[]> GetHeadersAsync(
        IEnumerable<byte[]> locatorHashes,
        CancellationToken cancellationToken)
      {
        await NetworkMessageStreamer.WriteAsync(
          new GetHeadersMessage(
            locatorHashes,
            ProtocolVersion));

        while (true)
        {
          NetworkMessage networkMessage = await ReceiveSessionMessageAsync(cancellationToken);

          if (networkMessage.Command == "headers")
          {
            return networkMessage.Payload;
          }
        }
      }

      public async Task RequestBlocksAsync(IEnumerable<byte[]> headerHashes)
      {
        await SendMessageAsync(
          new GetDataMessage(
            headerHashes.Select(h => new Inventory(InventoryType.MSG_BLOCK, h))));
      }
      public async Task<byte[]> ReceiveBlockAsync(CancellationToken cancellationToken)
      {
        while(true)
        {
          NetworkMessage networkMessage = await ReceiveSessionMessageAsync(cancellationToken)
            .ConfigureAwait(false);

          if (networkMessage.Command != "block")
          {
            continue;
          }

          return networkMessage.Payload;
        }
      }

      public string GetIdentification()
      {
        return IPEndPoint.Address.ToString();
      }
    }
  }
}
