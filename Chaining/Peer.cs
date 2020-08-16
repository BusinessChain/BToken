using System;
using System.Collections.Generic;
using System.Linq;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using System.Threading.Tasks.Dataflow;
using System.Net;
using System.Net.Sockets;



namespace BToken.Chaining
{
  partial class Blockchain
  {
    partial class Peer
    {
      enum StatusUTXOSyncSession
      {
        IDLE,
        BUSY,
        AWAITING_INSERTION,
        COMPLETED,
        DISPOSED
      }

      Blockchain Blockchain;

      public bool IsSynchronized;
      readonly object LOCK_Status = new object();
      StatusUTXOSyncSession Status;

      const int TIMEOUT_BLOCKDOWNLOAD_MILLISECONDS = 20000;
      const int TIMEOUT_GETHEADERS_MILLISECONDS = 5000;

      const int COUNT_BLOCKS_DOWNLOADBATCH_INIT = 2;
      Stopwatch StopwatchDownload = new Stopwatch();
      public int CountBlocksLoad = COUNT_BLOCKS_DOWNLOADBATCH_INIT;

      public Stack<UTXOTable.BlockArchive> BlockArchivesDownloaded =
        new Stack<UTXOTable.BlockArchive>();

      readonly object LOCK_IsExpectingMessageResponse = new object();
      bool IsExpectingMessageResponse;
            
      BufferBlock<NetworkMessage> MessageResponseBuffer =
        new BufferBlock<NetworkMessage>();
      BufferBlock<NetworkMessage> MessageInboundBuffer =
        new BufferBlock<NetworkMessage>();
      
      ulong FeeFilterValue;




      public enum ConnectionType { OUTBOUND, INBOUND };
      ConnectionType Connection;
      const UInt32 ProtocolVersion = 70015;
      TcpClient TcpClient;
      MessageStream MessageStream;

      public Peer(
        TcpClient tcpClient,
        Blockchain blockchain)
      {
        TcpClient = tcpClient;

        MessageStream = new MessageStream(
          tcpClient.GetStream());

        Connection = ConnectionType.INBOUND;
      }

      public Peer(Blockchain blockchain)
      {
        Connection = ConnectionType.OUTBOUND;
        Blockchain = blockchain;
      }
      
      async Task ProcessPingMessageAsync(NetworkMessage networkMessage)
      {
        PingMessage pingMessage = new PingMessage(networkMessage);

        await MessageStream.Write(
          new PongMessage(pingMessage.Nonce));
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
        => await MessageStream.Write(new SendHeadersMessage());


      public async Task Connect(IPAddress iPAddress, int port)
      {
        TcpClient = new TcpClient();

        await TcpClient.ConnectAsync(
          iPAddress,
          port);

        MessageStream = new MessageStream(
          TcpClient.GetStream());

        await HandshakeAsync();
      }

      const ServiceFlags NetworkServicesRemoteRequired = ServiceFlags.NODE_NETWORK;
      const ServiceFlags NetworkServicesLocal = ServiceFlags.NODE_NETWORK;
      const string UserAgent = "/BToken:0.0.0/";
      const Byte RelayOption = 0x00;
      static ulong Nonce = CreateNonce();
      static ulong CreateNonce()
      {
        Random rnd = new Random();

        ulong number = (ulong)rnd.Next();
        number = number << 32;
        return number |= (uint)rnd.Next();
      }

      async Task HandshakeAsync()
      {
        var versionMessage = new VersionMessage()
        {
          ProtocolVersion = ProtocolVersion,
          NetworkServicesLocal = (long)NetworkServicesLocal,
          UnixTimeSeconds = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
          NetworkServicesRemote = (long)NetworkServicesRemoteRequired,
          IPAddressRemote = IPAddress.Loopback.MapToIPv6(),
          PortRemote = Port,
          IPAddressLocal = IPAddress.Loopback.MapToIPv6(),
          PortLocal = Port,
          Nonce = Nonce,
          UserAgent = UserAgent,
          BlockchainHeight = Blockchain.Height,
          RelayOption = RelayOption
        };

        versionMessage.SerializePayload();

        await MessageStream.Write(versionMessage);

        CancellationToken cancellationToken =
          new CancellationTokenSource(TimeSpan.FromSeconds(3))
          .Token;

        bool verAckReceived = false;
        bool versionReceived = false;

        while (!verAckReceived || !versionReceived)
        {
          NetworkMessage messageRemote = await MessageStream
            .Read(cancellationToken);

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
                rejectionReason = string.Format(
                  "Outdated version '{0}', minimum expected version is '{1}'.",
                  versionMessageRemote.ProtocolVersion,
                  ProtocolVersion);
              }

              if (!((ServiceFlags)versionMessageRemote.NetworkServicesLocal)
                .HasFlag(NetworkServicesRemoteRequired))
              {
                rejectionReason = string.Format(
                  "Network services '{0}' do not meet requirement '{1}'.",
                  versionMessageRemote.NetworkServicesLocal,
                  NetworkServicesRemoteRequired);
              }

              if (versionMessageRemote.UnixTimeSeconds -
                DateTimeOffset.UtcNow.ToUnixTimeSeconds() > 2 * 60 * 60)
              {
                rejectionReason = string.Format(
                  "Unix time '{0}' more than 2 hours in the " +
                  "future compared to local time '{1}'.",
                  versionMessageRemote.NetworkServicesLocal,
                  NetworkServicesRemoteRequired);
              }

              if (versionMessageRemote.Nonce == Nonce)
              {
                rejectionReason = string.Format(
                  "Duplicate Nonce '{0}'.",
                  Nonce);
              }

              if (rejectionReason != "")
              {
                await MessageStream.Write(
                  new RejectMessage(
                    "version",
                    RejectMessage.RejectCode.OBSOLETE,
                    rejectionReason));

                throw new ChainException(
                  "Remote peer rejected: " + rejectionReason);
              }

              await MessageStream.Write(
                new VerAckMessage());
              break;

            case "reject":
              RejectMessage rejectMessage = new RejectMessage(
                messageRemote.Payload);

              throw new ChainException(
                string.Format("Peer rejected handshake: '{0}'",
                rejectMessage.RejectionReason));

            default:
              throw new ChainException(string.Format(
                "Received improper message '{0}' during handshake session.",
                messageRemote.Command));
          }
        }
      }



      public async Task Run()
      {
        StartListener();

        try
        {
          while (true)
          {
            NetworkMessage message = await MessageStream
              .Read(default)
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
                lock (LOCK_IsExpectingMessageResponse)
                {
                  if (IsExpectingMessageResponse)
                  {
                    MessageResponseBuffer.Post(message);
                    break;
                  }
                }

                MessageInboundBuffer.Post(message);
                break;
            }
          }
        }
        catch
        {
          Dispose();
        }
      }

      async Task StartListener()
      {
        try
        {
          while (true)
          {
            NetworkMessage message = await MessageInboundBuffer
              .ReceiveAsync()
              .ConfigureAwait(false);

            switch (message.Command)
            {
              //case "getdata":
              //  var getDataMessage = new GetDataMessage(message);

              //  getDataMessage.Inventories.ForEach(inv =>
              //  {
              //    Console.WriteLine("getdata {0}: {1} from {2}",
              //      inv.Type,
              //      inv.Hash.ToHexString(),
              //      GetIdentification());
              //  });

              //  foreach (byte[] block in Blockchain.UTXOTable.GetBlocks(
              //    getDataMessage.Inventories
              //    .Where(i => i.Type == InventoryType.MSG_BLOCK)
              //    .Select(i => i.Hash)))
              //  {
              //    await SendMessage(
              //      new NetworkMessage("block", block));
              //  }

              //  break;

              //case "getheaders":
              //  var getHeadersMessage = new GetHeadersMessage(message);

              //  Console.WriteLine("received getheaders locator[0] {0} from {1}",
              //    getHeadersMessage.HeaderLocator.First().ToHexString(),
              //    GetIdentification());

              //  if (!Network
              //    .Headerchain
              //    .Synchronizer
              //    .GetIsSyncingCompleted())
              //  {
              //    break;
              //  }

              //  var headers = Network.Headerchain.GetHeaders(
              //    getHeadersMessage.HeaderLocator,
              //    2000,
              //    getHeadersMessage.StopHash);

              //  await SendMessage(
              //    new HeadersMessage(headers));

              //  Console.WriteLine("sent {0} headers tip {1} to {2}",
              //    headers.Count,
              //    headers.Any() ? headers.First().HeaderHash.ToHexString() : "",
              //    GetIdentification());

              //  break;

              //case "inv":
              //  var invMessage = new InvMessage(message);

              //  foreach (Inventory inv in invMessage.Inventories
              //    .Where(inv => inv.Type == InventoryType.MSG_BLOCK).ToList())
              //  {
              //    Console.WriteLine("inv message {0} from {1}",
              //         inv.Hash.ToHexString(),
              //         GetIdentification());

              //    if (Network.Headerchain.TryReadHeader(
              //      inv.Hash,
              //      out Header headerAdvertized))
              //    {
              //      //Console.WriteLine(
              //      //  "Advertized block {0} already in chain",
              //      //  inv.Hash.ToHexString());

              //      break;
              //    }

              //    Headerchain.Synchronizer.LoadBatch();
              //    await Headerchain.Synchronizer.DownloadHeaders(channel);

              //    if (Headerchain.Synchronizer.TryInsertBatch())
              //    {
              //      if (!await UTXOTable.Synchronizer.TrySynchronize(channel))
              //      {
              //        Console.WriteLine(
              //          //      "Could not synchronize UTXO, with channel {0}",
              //          GetIdentification());
              //      }
              //    }
              //    else
              //    {
              //      Console.WriteLine(
              //        "Failed to insert header message from channel {0}",
              //        GetIdentification());
              //    }
              //  }

              //  break;

              case "headers":

                await Blockchain.ReceiveHeader(
                  message.Payload,
                  this);

                break;


              default:
                break;
            }
          }
        }
        catch (Exception ex)
        {
          Dispose(string.Format(
            "Exception {0} when syncing: \n{1}",
            ex.GetType(),
            ex.Message));
        }
      }
                 


      public async Task SendHeaders(List<Header> headers)
      {
        await MessageStream.Write(
          new HeadersMessage(headers));
      }



      public UTXOTable.BlockArchive BlockArchive = 
        new UTXOTable.BlockArchive();

      public async Task<Header> GetHeaders(Header locator)
      {
        return await GetHeaders(
          new List<Header>() { locator });
      }

      public async Task<Header> GetHeaders(List<Header> locator)
      {
        await MessageStream.Write(
          new GetHeadersMessage(locator, ProtocolVersion));

        int timeout = TIMEOUT_GETHEADERS_MILLISECONDS;
        CancellationTokenSource cancellation =
          new CancellationTokenSource(timeout);

        lock (LOCK_IsExpectingMessageResponse)
        {
          IsExpectingMessageResponse = true;
        }

        NetworkMessage networkMessage;

        while (true)
        {
          networkMessage = await MessageResponseBuffer
            .ReceiveAsync(cancellation.Token);

          if (networkMessage.Command == "headers")
          {
            break;
          }
        }

        lock (LOCK_IsExpectingMessageResponse)
        {
          IsExpectingMessageResponse = false;
        }

        BlockArchive.Parse(networkMessage.Payload);

        if(BlockArchive.HeaderRoot != null)
        {
          Header headerLocatorAncestor = locator.Find(h =>
         h.Hash.IsEqual(BlockArchive.HeaderRoot.HashPrevious));

          if (headerLocatorAncestor == null)
          {
            throw new ChainException(
              "GetHeaders does not connect to locator.");
          }

          BlockArchive.HeaderRoot.HeaderPrevious = headerLocatorAncestor;
        }

        return BlockArchive.HeaderRoot;
      }

      public async Task<Header> SkipDuplicates(Header header, List<Header> locator)
      {
        Header headerAncestor = header.HeaderPrevious;

        Header stopHeader = locator[
          locator.IndexOf(headerAncestor) + 1];

        while (headerAncestor.HeaderNext.Hash
          .IsEqual(header.Hash))
        {
          headerAncestor = headerAncestor.HeaderNext;

          if (headerAncestor == stopHeader)
          {
            throw new ChainException(
              "Received headers do root in locator more than once.");
          }

          if (header.HeaderNext != null)
          {
            header = header.HeaderNext;
          }
          else
          {
            header = await GetHeaders(header);
          }
        }

        return header;
      }


      List<Inventory> Inventories = new List<Inventory>();

      public void CreateInventories(ref Header headerLoad)
      {
        Inventories.Clear();

        do
        {
          Inventories.Add(new Inventory(
            InventoryType.MSG_BLOCK,
            headerLoad.Hash));

          headerLoad = headerLoad.HeaderNext;
        } while (
           headerLoad != null &&
           Inventories.Count < CountBlocksLoad);
      }

      public async Task<bool> TryDownloadBlocks()
      {
        try
        {
          var cancellation = new CancellationTokenSource(
              TIMEOUT_BLOCKDOWNLOAD_MILLISECONDS);
                   
          await MessageStream.Write(
            new GetDataMessage(Inventories));

          Console.WriteLine(
            "Sent {0} inventories to peer {1} \n {2} -> {3}",
            Inventories.Count,
            GetIdentification(),
            Inventories.First().Hash.ToHexString(),
            Inventories.Last().Hash.ToHexString());

          StopwatchDownload.Restart();

          lock (LOCK_IsExpectingMessageResponse)
          {
            IsExpectingMessageResponse = true;
          }
          
          foreach(Inventory inventory in Inventories)
          {
            NetworkMessage networkMessage = await MessageResponseBuffer
              .ReceiveAsync(cancellation.Token)
              .ConfigureAwait(false);

            switch (networkMessage.Command)
            {
              case "notfound":
                SetStatusCompleted();
                return false;

              case "block":                
                BlockArchive.Parse(networkMessage.Payload);

                if(BlockArchive.HeaderRoot == null ||
                  !inventory.Hash.IsEqual(BlockArchive.HeaderRoot.Hash))
                {
                  throw new ChainException(string.Format(
                    "Requested block {0} but received {1}",
                    inventory.Hash.ToHexString(),
                    BlockArchive.HeaderRoot == null ? 
                    "none" : BlockArchive.HeaderRoot.Hash.ToHexString()));
                }
                
                Console.WriteLine(
                  "Received block {0}",
                  inventory.Hash.ToHexString());
                break;

              default:
                throw new ChainException(string.Format(
                  "Requested block {0} but received message {1}",
                  inventory.Hash.ToHexString(),
                  networkMessage.Command));

            }
          }

          lock (LOCK_IsExpectingMessageResponse)
          {
            IsExpectingMessageResponse = false;
          }

          StopwatchDownload.Stop();
        }
        catch (Exception ex)
        {
          Console.WriteLine(
            "Exception {0} in download of blockArchive {1}: \n{2}",
            ex.GetType().Name,
            BlockArchive.Index,
            ex.Message);

          Dispose();

          return false;
        }
        
        BlockArchivesDownloaded.Push(BlockArchive);

        CalculateNewCountBlocks();

        return true;
      }

      public void CalculateNewCountBlocks()
      {
        const float safetyFactorTimeout = 10;
        const float marginFactorResetCountBlocksDownload = 5;

        float ratioTimeoutToDownloadTime = TIMEOUT_BLOCKDOWNLOAD_MILLISECONDS
          / (1 + StopwatchDownload.ElapsedMilliseconds);

        if (ratioTimeoutToDownloadTime > safetyFactorTimeout)
        {
          CountBlocksLoad += 1;
        }
        else if (ratioTimeoutToDownloadTime <
          marginFactorResetCountBlocksDownload)
        {
          CountBlocksLoad = COUNT_BLOCKS_DOWNLOADBATCH_INIT;
        }
        else if (CountBlocksLoad > 1)
        {
          CountBlocksLoad -= 1;
        }
      }



      public List<byte[]> HeaderDuplicates = new List<byte[]>();

      public bool IsInbound()
      {
        return Connection == ConnectionType.INBOUND;
      }

      public string GetIdentification()
      {
        string signConnectionType =
          Connection == ConnectionType.INBOUND ? " <- " : " -> ";

        return
          TcpClient.Client.LocalEndPoint.ToString() +
          signConnectionType +
          TcpClient.Client.RemoteEndPoint.ToString();
      }


      public void SetStatusCompleted()
      {
        lock (LOCK_Status)
        {
          Status = StatusUTXOSyncSession
            .COMPLETED;
        }
      }
      public bool IsStatusCompleted()
      {
        lock (LOCK_Status)
        {
          return Status == StatusUTXOSyncSession
            .COMPLETED;
        }
      }

      public void Dispose(string message)
      {
        Console.WriteLine(string.Format(
          "Dispose peer {0}: \n{1}",
          GetIdentification(),
          message));

        Dispose();
      }


      public void Dispose()
      {        
        TcpClient.Dispose();

        Console.WriteLine("Dispose peer {0}", GetIdentification());

        lock (LOCK_Status)
        {
          Status = StatusUTXOSyncSession
            .DISPOSED;
        }
      }

      public bool IsStatusDisposed()
      {
        lock (LOCK_Status)
        {
          return Status == StatusUTXOSyncSession
            .DISPOSED;
        }
      }
      public void SetStatusBusy()
      {
        lock (LOCK_Status)
        {
          Status = StatusUTXOSyncSession
            .BUSY;
        }
      }
      public void SetStatusAwaitingInsertion()
      {
        lock (LOCK_Status)
        {
          Status = StatusUTXOSyncSession
            .AWAITING_INSERTION;
        }
      }
      public bool IsStatusAwaitingInsertion()
      {
        lock (LOCK_Status)
        {
          return Status == StatusUTXOSyncSession
            .AWAITING_INSERTION;
        }
      }
      public void SetStatusIdle()
      {
        lock (LOCK_Status)
        {
          Status = StatusUTXOSyncSession
            .IDLE;
        }
      }
      public bool IsStatusIdle()
      {
        lock (LOCK_Status)
        {
          return Status == StatusUTXOSyncSession
            .IDLE;
        }
      }
      public bool IsStatusBusy()
      {
        lock (LOCK_Status)
        {
          return Status == StatusUTXOSyncSession
            .BUSY;
        }
      }

    }
  }
}
