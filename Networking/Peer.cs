﻿using System.Diagnostics;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using System.Threading.Tasks.Dataflow;

using BToken.Chaining;

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

      VersionMessage VersionMessageRemote;

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
          Dispose();
          return false;
        }
      }


      async Task Connect()
      {
        IPAddress iPAddress = Network.AddressPool.GetRandomNodeAddress();
        IPEndPoint = new IPEndPoint(iPAddress, Port);
        await ConnectTCPAsync();
        await HandshakeAsync();
      }

      async Task ProcessNetworkMessagesAsync()
      {
        while (true)
        {
          NetworkMessage message = await NetworkMessageStreamer
            .ReadAsync(default).ConfigureAwait(false);

          switch (message.Command)
          {
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
            case "headers":
              await ProcessHeadersMessage(
                new HeadersMessage(message));
              break;
            default:
              ProcessApplicationMessage(message);
              break;
          }
        }
      }
      async Task ProcessHeadersMessage(HeadersMessage headersMessage)
      {
        Console.WriteLine("header message from channel {0}",
          GetIdentification());

        if (Network.Headerchain.TryInsertHeaderBytes(
          headersMessage.Payload))
        {
          while (Network.UTXOTable.TryLoadBatch(
            out DataBatch batch, 1))
          {
            try
            {
              await DownloadBlocks(batch);
            }
            catch
            {
              Console.WriteLine("could not download batch {0} from {1}",
                batch.Index,
                GetIdentification());

              Network.UTXOTable.UnLoadBatch(batch);
              break;
            }


            if (Network.UTXOTable.TryInsertBatch(batch))
            {
              Console.WriteLine("inserted batch {0} in UTXO table",
                batch.Index);
            }
            else
            {
              Console.WriteLine("could not insert batch {0} in UTXO table",
                batch.Index);
            }
          }          
        }
        else
        {
          Console.WriteLine("Failed to insert header message from channel {0}",
            GetIdentification());
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

        await TcpClient.ConnectAsync(
          IPEndPoint.Address, 
          IPEndPoint.Port);

        NetworkMessageStreamer = new MessageStreamer(TcpClient.GetStream());
      }
      async Task HandshakeAsync()
      {
        await NetworkMessageStreamer.WriteAsync(new VersionMessage());
        
        CancellationToken cancellationToken = new CancellationTokenSource(
          TimeSpan.FromSeconds(3)).Token;
        
        bool VerAckReceived = false;
        bool VersionReceived = false;

        while (!VerAckReceived || !VersionReceived)
        {
          NetworkMessage messageRemote = 
            await NetworkMessageStreamer.ReadAsync(cancellationToken);

          switch (messageRemote.Command)
          {
            case "verack":
              VerAckReceived = true;
              break;

            case "version":
              VersionMessageRemote = new VersionMessage(messageRemote.Payload);
              VersionReceived = true;
              await ProcessVersionMessageRemoteAsync().ConfigureAwait(false);
              break;

            case "reject":
              RejectMessage rejectMessage = new RejectMessage(messageRemote.Payload);
              throw new NetworkException(string.Format("Peer rejected handshake: '{0}'", rejectMessage.RejectionReason));

            default:
              throw new NetworkException(string.Format("Handshake aborted: Received improper message '{0}' during handshake session.", messageRemote.Command));
          }
        }
      }
      async Task ProcessVersionMessageRemoteAsync()
      {
        string rejectionReason = "";

        if (VersionMessageRemote.ProtocolVersion < ProtocolVersion)
        {
          rejectionReason = string.Format("Outdated version '{0}', minimum expected version is '{1}'.",
            VersionMessageRemote.ProtocolVersion, ProtocolVersion);
        }

        if (!((ServiceFlags)VersionMessageRemote.NetworkServicesLocal).HasFlag(NetworkServicesRemoteRequired))
        {
          rejectionReason = string.Format("Network services '{0}' do not meet requirement '{1}'.",
            VersionMessageRemote.NetworkServicesLocal, NetworkServicesRemoteRequired);
        }

        if (VersionMessageRemote.UnixTimeSeconds - GetUnixTimeSeconds() > 2 * 60 * 60)
        {
          rejectionReason = string.Format("Unix time '{0}' more than 2 hours in the future compared to local time '{1}'.", 
            VersionMessageRemote.NetworkServicesLocal, NetworkServicesRemoteRequired);
        }

        if (VersionMessageRemote.Nonce == Nonce)
        {
          rejectionReason = string.Format("Duplicate Nonce '{0}'.", Nonce);
        }

        if (rejectionReason != "")
        {
          await SendMessage(
            new RejectMessage(
              "version", 
              RejectMessage.RejectCode.OBSOLETE, 
              rejectionReason)).ConfigureAwait(false);

          throw new NetworkException("Remote peer rejected: " + rejectionReason);
        }

        await SendMessage(new VerAckMessage());
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
      async Task ProcessSendHeadersMessageAsync(NetworkMessage networkMessage) 
        => await NetworkMessageStreamer.WriteAsync(new SendHeadersMessage());

      public async Task SendMessage(NetworkMessage networkMessage)
      {
        await NetworkMessageStreamer.WriteAsync(networkMessage);
      }

      public async Task<NetworkMessage> ReceiveApplicationMessage(
        CancellationToken cancellationToken)
      {
        return await ApplicationMessages.ReceiveAsync(cancellationToken);
      }

      public async Task PingAsync()
      {
        await NetworkMessageStreamer
          .WriteAsync(new PingMessage(Nonce));
      }

      public async Task<byte[]> GetHeaders(
        IEnumerable<byte[]> locatorHashes,
        CancellationToken cancellationToken)
      {
        await NetworkMessageStreamer.WriteAsync(
          new GetHeadersMessage(
            locatorHashes,
            ProtocolVersion));

        while (true)
        {
          NetworkMessage networkMessage = await ReceiveApplicationMessage(cancellationToken);

          if (networkMessage.Command == "headers")
          {
            return networkMessage.Payload;
          }
        }
      }



      const int TIMEOUT_BLOCKDOWNLOAD_MILLISECONDS = 20000;

      public async Task DownloadBlocks(DataBatch batch)
      {
        IEnumerable<Inventory> inventories = batch.ItemBatchContainers
          .Where(b => b.Buffer == null)
          .Select(b => new Inventory(
            InventoryType.MSG_BLOCK,
            ((UTXOTable.BlockBatchContainer)b).Header.HeaderHash));

        await SendMessage(
          new GetDataMessage(inventories));

        var cancellationDownloadBlocks =
          new CancellationTokenSource(TIMEOUT_BLOCKDOWNLOAD_MILLISECONDS);

        foreach (DataContainer blockBatchContainer 
          in batch.ItemBatchContainers)
        {
          while (blockBatchContainer.Buffer == null)
          {
            NetworkMessage message = await NetworkMessageStreamer
              .ReadAsync(default).ConfigureAwait(false);
                        
            if (message.Command != "block")
            {
              continue;
            }

            blockBatchContainer.Buffer = message.Payload;
            blockBatchContainer.TryParse();
          }
        }
      }


      public string GetIdentification()
      {
        return IPEndPoint.Address.ToString();
      }
    }
  }
}
