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
    enum ConnectionType { OUTBOUND, INBOUND };

    partial class Peer : INetworkChannel
    {
      Network Network;
      Blockchain Blockchain;

      public IPEndPoint IPEndPoint;
      TcpClient TcpClient;
      MessageStreamer NetworkMessageStreamer;

      VersionMessage VersionMessageRemote;

      readonly object LOCK_IsDispatched = new object();
      bool IsDispatched = true;

      BufferBlock<NetworkMessage> ApplicationMessages =
        new BufferBlock<NetworkMessage>();

      ulong FeeFilterValue;

      public ConnectionType ConnectionType;




      public Peer(
        IPEndPoint iPEndPoint,
        ConnectionType connectionType,
        Network network)
      {
        IPEndPoint = iPEndPoint;
        ConnectionType = connectionType;
        Network = network;
      }

      public Peer(
        TcpClient tcpClient, 
        ConnectionType connectionType,
        Network network)
        : this(
            (IPEndPoint)tcpClient.Client.RemoteEndPoint,
            connectionType,
            network)
      {
        TcpClient = tcpClient;

        NetworkMessageStreamer = new MessageStreamer(
          tcpClient.GetStream());

        IPEndPoint = (IPEndPoint)tcpClient.Client.RemoteEndPoint;
      }

      public async Task Start()
      {
        try
        {
          await HandshakeAsync();

          Release();

          await ProcessNetworkMessages();
        }
        catch(Exception ex)
        {
          Dispose();
        }
      }


      public void Dispose()
      {
        TcpClient.Dispose();

        lock (Network.LOCK_Peers)
        {
          Network.Peers.Remove(this);

          Console.WriteLine(
            "disposed {0} peer {1}, total peers {2}",
            ConnectionType.ToString(),
            IPEndPoint,
            Network.Peers.Count);
        }

        if (ConnectionType == ConnectionType.OUTBOUND)
        {
          Network.CreateOutboundPeer();
        }
      }

      async Task ProcessNetworkMessages()
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

            default:
              lock (LOCK_IsDispatched)
              {
                if (IsDispatched)
                {
                  ApplicationMessages.Post(message);
                  break;
                }
                else
                {
                  IsDispatched = true;
                }
              }

              await ProcessRequest(message);

              Release();

              break;
          }
        }
      }


      async Task ProcessRequest(NetworkMessage message)
      {
        switch (message.Command)
        {
          case "getdata":
            var getDataMessage = new GetDataMessage(message);

            getDataMessage.Inventories.ForEach(inv =>
            {
              Console.WriteLine("getdata {0}: {1} from {2}",
                inv.Type,
                inv.Hash.ToHexString(),
                GetIdentification());
            });

            foreach(byte[] block in Network.UTXOTable.Synchronizer.GetBlocks(
              getDataMessage.Inventories
              .Where(i => i.Type == InventoryType.MSG_BLOCK)
              .Select(i => i.Hash)))
            {
              await SendMessage(
                new NetworkMessage("block", block));
            }

            break;

          case "getheaders":
            var getHeadersMessage = new GetHeadersMessage(message);

            Console.WriteLine("received getheaders locator[0] {0} from {1}",
              getHeadersMessage.HeaderLocator.First().ToHexString(),
              GetIdentification());

            if(!Network
              .Headerchain
              .Synchronizer
              .GetIsSyncingCompleted())
            {
              break;
            }

            var headers = Network.Headerchain.GetHeaders(
              getHeadersMessage.HeaderLocator,
              2000,
              getHeadersMessage.StopHash);

            await SendMessage(
              new HeadersMessage(headers));

            Console.WriteLine("sent {0} headers tip {1} to {2}",
              headers.Count,
              headers.Any() ? headers.First().HeaderHash.ToHexString() : "",
              GetIdentification());

            break;

          case "inv":
            var invMessage = new InvMessage(message);

            //foreach (Inventory inv in invMessage.Inventories
            //  .Where(inv => inv.Type == InventoryType.MSG_BLOCK).ToList())
            //{
            //  Console.WriteLine("inv message {0} from {1}",
            //       inv.Hash.ToHexString(),
            //       GetIdentification());

            //  if (Network.Headerchain.TryReadHeader(
            //    inv.Hash,
            //    out Header headerAdvertized))
            //  {
            //    //Console.WriteLine(
            //    //  "Advertized block {0} already in chain",
            //    //  inv.Hash.ToHexString());

            //    break;
            //  }

            //  Headerchain.Synchronizer.LoadBatch();
            //  await Headerchain.Synchronizer.DownloadHeaders(channel);

            //  if (Headerchain.Synchronizer.TryInsertBatch())
            //  {
            //    if (!await UTXOTable.Synchronizer.TrySynchronize(channel))
            //    {
            //      Console.WriteLine(
            //        //      "Could not synchronize UTXO, with channel {0}",
            //        GetIdentification());
            //    }
            //  }
            //  else
            //  {
            //    Console.WriteLine(
            //      "Failed to insert header message from channel {0}",
            //      GetIdentification());
            //  }
            //}

            break;

          case "headers":

            await Blockchain.InsertHeaders(
              message.Payload,
              new Blockchain.BlockchainChannel(this));
                        
            break;


          default:
            break;
        }
      }



      public bool TryDispatch()
      {
        lock (LOCK_IsDispatched)
        {
          if(IsDispatched)
          {
            return false;
          }

          IsDispatched = true;
          return true;
        }
      }



      public void Release()
      {
        lock (LOCK_IsDispatched)
        {
          IsDispatched = false;
        }
      }

      public List<NetworkMessage> GetApplicationMessages()
      {
        if (ApplicationMessages.TryReceiveAll(out IList<NetworkMessage> messages))
        {
          return (List<NetworkMessage>)messages;
        }

        return new List<NetworkMessage>();
      }

      public async Task<bool> TryConnect()
      {
        try
        {
          TcpClient = new TcpClient();

          await TcpClient.ConnectAsync(
            IPEndPoint.Address,
            IPEndPoint.Port);

          NetworkMessageStreamer = new MessageStreamer(
            TcpClient.GetStream());

          return true;
        }
        catch
        {
          return false;
        }
      }
      async Task HandshakeAsync()
      {
        await NetworkMessageStreamer.WriteAsync(new VersionMessage());

        CancellationToken cancellationToken = new CancellationTokenSource(TimeSpan.FromSeconds(3))
          .Token;

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

        if (VersionMessageRemote.UnixTimeSeconds - 
          DateTimeOffset.UtcNow.ToUnixTimeSeconds() > 2 * 60 * 60)
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



      
      const int TIMEOUT_BLOCKDOWNLOAD_MILLISECONDS = 5000;


      public async Task RequestBlocks(List<byte[]> hashes)
      {
        await SendMessage(
          new GetDataMessage(
            hashes.Select(h => new Inventory(
              InventoryType.MSG_BLOCK, h))
              .ToList()));
      }

      public async Task<byte[]> ReceiveBlock(CancellationToken cancellationToken)
      {
        var cancellation = new CancellationTokenSource(
          TIMEOUT_BLOCKDOWNLOAD_MILLISECONDS);

        while (true)
        {
          NetworkMessage networkMessage = 
            await ApplicationMessages.ReceiveAsync(cancellationToken)
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
        string signConnectionType =
          ConnectionType == ConnectionType.INBOUND ? " <- " : " -> ";

        return 
          TcpClient.Client.LocalEndPoint.ToString() + 
          signConnectionType + 
          TcpClient.Client.RemoteEndPoint.ToString();
      }

    }
  }
}