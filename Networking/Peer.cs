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
    partial class Peer : IDisposable, INetworkChannel
    {
      Network Network;

      public IPEndPoint IPEndPoint { get; private set; }
      TcpClient TcpClient;
      MessageStreamer NetworkMessageStreamer;

      readonly object IsDispatchedLOCK = new object();
      bool IsDispatched = true;

      public BufferBlock<NetworkMessage> ApplicationMessageBuffer;

      ulong FeeFilterValue;


      public Peer(Network network)
      {
        Network = network;

        ApplicationMessageBuffer = new BufferBlock<NetworkMessage>(
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

          Release();

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
          Dispose();
        }
      }

      async Task ConnectAsync()
      {
        IPAddress iPAddress = Network.AddressPool.GetRandomNodeAddress();
        IPEndPoint = new IPEndPoint(iPAddress, Port);
        await ConnectTCPAsync();
      }

      async Task ProcessNetworkMessagesAsync()
      {
        while (true)
        {
          NetworkMessage networkMessage = await NetworkMessageStreamer.ReadAsync(default(CancellationToken));

          switch (networkMessage.Command)
          {
            case "version":
              await ProcessVersionMessageAsync(networkMessage, default(CancellationToken));
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
              ProcessApplicationMessage(networkMessage);
              break;
          }
        }
      }
      void ProcessApplicationMessage(NetworkMessage networkMessage)
      {
        ApplicationMessageBuffer.Post(networkMessage);
        
        if (TryDispatch())
        {
          Network.PeerRequestInboundBuffer.Post(this);
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

      public List<NetworkMessage> GetRequestMessages()
      {
        ApplicationMessageBuffer.TryReceiveAll(out IList<NetworkMessage> requestMessages);
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
      //async Task ProcessInventoryMessageAsync(NetworkMessage networkMessage)
      //{
      //  InvMessage invMessage = new InvMessage(networkMessage);

      //  if (invMessage.GetBlockInventories().Any()) // direkt als property zu kreationszeit anlegen.
      //  {
      //    await Network.NetworkMessageBufferBlockchain.SendAsync(invMessage).ConfigureAwait(false);
      //  }
      //  if (invMessage.GetTXInventories().Any())
      //  {
      //    await Network.NetworkMessageBufferUTXO.SendAsync(invMessage).ConfigureAwait(false);
      //  };
      //}
      
      public async Task SendMessageAsync(NetworkMessage networkMessage) => await NetworkMessageStreamer.WriteAsync(networkMessage).ConfigureAwait(false);

      public void Dispose()
      {
        TcpClient.Close();
      }
      
      public async Task PingAsync() => await NetworkMessageStreamer.WriteAsync(new PingMessage(Nonce));

      public async Task<NetworkBlock> GetBlockAsync(UInt256 hash, CancellationToken cancellationToken)
      {
        var inventory = new Inventory(InventoryType.MSG_BLOCK, hash);
        await NetworkMessageStreamer.WriteAsync(new GetDataMessage(new List<Inventory>() { inventory }));

        while (true)
        {
          NetworkMessage networkMessage = await ApplicationMessageBuffer.ReceiveAsync(cancellationToken);
          var blockMessage = networkMessage as BlockMessage;

          if (blockMessage != null)
          {
            return blockMessage.NetworkBlock;
          }
        }
      }
      public async Task<List<NetworkHeader>> GetHeadersAsync(List<UInt256> headerLocator)
      {
        await NetworkMessageStreamer.WriteAsync(new GetHeadersMessage(headerLocator));

        CancellationToken cancellationToken = new CancellationTokenSource(TimeSpan.FromSeconds(2)).Token;
        HeadersMessage headersMessage = await ReceiveHeadersMessageAsync(cancellationToken);
        return headersMessage.Headers;
      }
      async Task<HeadersMessage> ReceiveHeadersMessageAsync(CancellationToken cancellationToken)
      {
        while (true)
        {
          NetworkMessage networkMessage = await ApplicationMessageBuffer.ReceiveAsync(cancellationToken);
          HeadersMessage headersMessage = networkMessage as HeadersMessage;

          if (headersMessage != null)
          {
            return headersMessage;
          }
        }
      }

      public void ReportSessionSuccess(INetworkSession session)
      {

      }
      public void ReportSessionFail(INetworkSession session)
      {

      }

    }
  }
}
