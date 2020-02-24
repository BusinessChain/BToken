using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using System.Threading.Tasks.Dataflow;

using BToken.Blockchain;

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

      readonly object LOCK_IsDispatched = new object();
      bool IsDispatched = true;

      BufferBlock<NetworkMessage> ApplicationMessages =
        new BufferBlock<NetworkMessage>();

      ulong FeeFilterValue;

      public ConnectionType ConnectionType;




      public Peer(
        IPAddress iPAddress,
        ConnectionType connectionType,
        Network network)
      {
        IPEndPoint = new IPEndPoint(iPAddress, Port);
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

      public async Task Connect()
      {
        TcpClient = new TcpClient();

        await TcpClient.ConnectAsync(
          IPEndPoint.Address,
          IPEndPoint.Port);

        NetworkMessageStreamer = new MessageStreamer(
          TcpClient.GetStream());

        await HandshakeAsync();
      }

      
      public async Task Run()
      {

        Release();


        // Will that throw an exception when in some Application session 
        // an exception is thrown. Or is it, that in the app always a timeout
        // occurs when here an exception is thrown. Can it be that both the app and 
        // the network attempt to renew a peer in case of an exception.
        await ProcessNetworkMessages();
      }

      readonly object LOCK_FlagIsDisposed;
      bool FlagIsDisposed;

      public void Dispose()
      {
        lock (LOCK_FlagIsDisposed)
        {
          FlagIsDisposed = true;
        }

        TcpClient.Dispose();
      }

      public bool IsDisposed()
      {
        lock (LOCK_FlagIsDisposed)
        {
          return FlagIsDisposed;
        }
      }



      public async Task ProcessNetworkMessages()
      {
        try
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
                ApplicationMessages.Post(message);
                break;
            }
          }
        }
        catch
        {
          Dispose();
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
              new Blockchain.BlockchainPeer(this));
                        
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

      async Task HandshakeAsync()
      {
        await NetworkMessageStreamer.WriteAsync(new VersionMessage());

        CancellationToken cancellationToken = new CancellationTokenSource(TimeSpan.FromSeconds(3))
          .Token;

        bool verAckReceived = false;
        bool versionReceived = false;

        while (!verAckReceived || !versionReceived)
        {
          NetworkMessage messageRemote =
            await NetworkMessageStreamer.ReadAsync(cancellationToken);

          switch (messageRemote.Command)
          {
            case "verack":
              verAckReceived = true;
              break;

            case "version":
              var versionMessageRemote = new VersionMessage(messageRemote.Payload);

              versionReceived = true;
              string rejectionReason = "";

              if (versionMessageRemote.ProtocolVersion < ProtocolVersion)
              {
                rejectionReason = string.Format("Outdated version '{0}', minimum expected version is '{1}'.",
                  versionMessageRemote.ProtocolVersion, ProtocolVersion);
              }

              if (!((ServiceFlags)versionMessageRemote.NetworkServicesLocal).HasFlag(NetworkServicesRemoteRequired))
              {
                rejectionReason = string.Format("Network services '{0}' do not meet requirement '{1}'.",
                  versionMessageRemote.NetworkServicesLocal, NetworkServicesRemoteRequired);
              }

              if (versionMessageRemote.UnixTimeSeconds -
                DateTimeOffset.UtcNow.ToUnixTimeSeconds() > 2 * 60 * 60)
              {
                rejectionReason = string.Format("Unix time '{0}' more than 2 hours in the future compared to local time '{1}'.",
                  versionMessageRemote.NetworkServicesLocal, NetworkServicesRemoteRequired);
              }

              if (versionMessageRemote.Nonce == Nonce)
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
              break;

            case "reject":
              RejectMessage rejectMessage = new RejectMessage(messageRemote.Payload);
              throw new NetworkException(string.Format("Peer rejected handshake: '{0}'", rejectMessage.RejectionReason));

            default:
              throw new NetworkException(string.Format("Handshake aborted: Received improper message '{0}' during handshake session.", messageRemote.Command));
          }
        }
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

      public async Task<NetworkMessage> ReceiveMessage(
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