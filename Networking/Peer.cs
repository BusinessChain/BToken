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
      {
        TcpClient = tcpClient;

        NetworkMessageStreamer = new MessageStreamer(
          tcpClient.GetStream());

        IPEndPoint = (IPEndPoint)tcpClient.Client.RemoteEndPoint;

        ConnectionType = connectionType;
        Network = network;
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

      readonly object LOCK_FlagIsDisposed;
      bool FlagIsDisposed;

      public void Dispose()
      {
        // Der peer soll grundsätzlich nicht
        // häufig reconnecten, daher führt einfach jedes Disposen
        // zu Blame.
        Blame();

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



      // Will that throw an exception when in some Application session 
      // an exception is thrown. Or is it, that in the app always a timeout
      // occurs when here an exception is thrown. Can it be that both the app and 
      // the network attempt to renew a peer in case of an exception.

      public async Task Run()
      {
        try
        {
          while (true)
          {
            NetworkMessage message = await NetworkMessageStreamer
              .ReadAsync(default)
              .ConfigureAwait(false);

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

      async Task ProcessPingMessageAsync(NetworkMessage networkMessage)
      {
        PingMessage pingMessage = new PingMessage(networkMessage);
        await NetworkMessageStreamer.WriteAsync(
          new PongMessage(pingMessage.Nonce)).ConfigureAwait(false);
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