using System;
using System.Collections.Generic;
using System.Linq;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using System.Threading.Tasks.Dataflow;
using System.Net;
using System.Net.Sockets;
using System.IO;
using System.Security.Cryptography;
using System.Text;



namespace BToken.Chaining
{
  partial class Blockchain
  {
    partial class Peer
    {
      Blockchain Blockchain;
      
      public bool IsBusy;

      public bool FlagDispose;
      public bool IsSynchronized;
      public bool IsSyncMaster;

      const int TIMEOUT_BLOCKDOWNLOAD_MILLISECONDS = 3000;
      const int TIMEOUT_GETHEADERS_MILLISECONDS = 3000;

      const int COUNT_BLOCKS_DOWNLOADBATCH_INIT = 1;
      Stopwatch StopwatchDownload = new Stopwatch();
      public int CountBlocksLoad = COUNT_BLOCKS_DOWNLOADBATCH_INIT;

      public UTXOTable.BlockParser BlockParser = 
        new UTXOTable.BlockParser();

      readonly object LOCK_IsExpectingMessageResponse = new object();
      bool IsExpectingMessageResponse;

      BufferBlock<bool> MessageResponseReady =
        new BufferBlock<bool>();
      BufferBlock<NetworkMessage> MessageInboundBuffer =
        new BufferBlock<NetworkMessage>();

      ulong FeeFilterValue;

      const ServiceFlags NetworkServicesRemoteRequired = 
        ServiceFlags.NODE_NONE;

      const ServiceFlags NetworkServicesLocal = 
        ServiceFlags.NODE_NETWORK;

      const string UserAgent = "/BToken:0.0.0/";
      const Byte RelayOption = 0x00;
      readonly static ulong Nonce = CreateNonce();
      static ulong CreateNonce()
      {
        Random rnd = new Random();

        ulong number = (ulong)rnd.Next();
        number = number << 32;
        return number |= (uint)rnd.Next();
      }
      public enum ConnectionType { OUTBOUND, INBOUND };
      ConnectionType Connection;
      const UInt32 ProtocolVersion = 70015;
      public IPAddress IPAddress;
      TcpClient TcpClient;
      NetworkStream NetworkStream;

      const int CommandSize = 12;
      const int LengthSize = 4;
      const int ChecksumSize = 4;

      public string Command;

      const int SIZE_MESSAGE_PAYLOAD_BUFFER = 0x400000;
      byte[] Payload = new byte[SIZE_MESSAGE_PAYLOAD_BUFFER];
      int PayloadLength;

      const int HeaderSize = CommandSize + LengthSize + ChecksumSize;
      byte[] MeassageHeader = new byte[HeaderSize];
      byte[] MagicBytes = new byte[4] { 0xF9, 0xBE, 0xB4, 0xD9 };

      SHA256 SHA256 = SHA256.Create();

      DirectoryInfo DirectoryLogPeers;
      DirectoryInfo DirectoryLogPeersDisposed;
      StreamWriter LogFile;
      string PathLogFile;



      public Peer(
        TcpClient tcpClient,
        Blockchain blockchain)
      {
        TcpClient = tcpClient;

        NetworkStream = tcpClient.GetStream();

        Connection = ConnectionType.INBOUND;

        CreateLogFile();
      }

      public Peer(Blockchain blockchain, IPAddress iPAddress)
      {
        Connection = ConnectionType.OUTBOUND;
        Blockchain = blockchain;
        IPAddress = iPAddress;

        CreateLogFile();
      }


      void CreateLogFile()
      {
        DirectoryLogPeers = Directory.CreateDirectory(
          "logPeers");

        PathLogFile = Path.Combine(
          DirectoryLogPeers.Name,
          GetID());

        LogFile = new StreamWriter(PathLogFile, true);
      }

      public async Task Connect(int port)
      {
        string.Format(
          "Connect peer {0}",
          GetID())
          .Log(LogFile);

        TcpClient = new TcpClient();

        await TcpClient.ConnectAsync(
          IPAddress,
          port);

        NetworkStream = TcpClient.GetStream();

        await HandshakeAsync(port);

        string.Format(
          "Network protocol handshake {0}",
          GetID())
          .Log(LogFile);

        StartMessageListener();
      }

      async Task HandshakeAsync(int port)
      {
        var versionMessage = new VersionMessage()
        {
          ProtocolVersion = ProtocolVersion,
          NetworkServicesLocal = (long)NetworkServicesLocal,
          UnixTimeSeconds = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
          NetworkServicesRemote = (long)NetworkServicesRemoteRequired,
          IPAddressRemote = IPAddress.Loopback.MapToIPv6(),
          PortRemote = (ushort)port,
          IPAddressLocal = IPAddress.Loopback.MapToIPv6(),
          PortLocal = (ushort)port,
          Nonce = Nonce,
          UserAgent = UserAgent,
          BlockchainHeight = Blockchain.Height,
          RelayOption = RelayOption
        };

        versionMessage.SerializePayload();

        await SendMessage(versionMessage);

        CancellationToken cancellationToken =
          new CancellationTokenSource(TimeSpan.FromSeconds(3))
          .Token;

        bool verAckReceived = false;
        bool versionReceived = false;

        while (!verAckReceived || !versionReceived)
        {
          await ReadMessage(cancellationToken);

          switch (Command)
          {
            case "verack":
              verAckReceived = true;
              break;

            case "version":
              var versionMessageRemote = new VersionMessage(Payload);

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
                await SendMessage(
                  new RejectMessage(
                    "version",
                    RejectMessage.RejectCode.OBSOLETE,
                    rejectionReason));

                throw new ChainException(
                  "Remote peer rejected: " + rejectionReason);
              }

              await SendMessage(new VerAckMessage());
              break;

            case "reject":
              RejectMessage rejectMessage = new RejectMessage(Payload);

              throw new ChainException(
                string.Format("Peer rejected handshake: '{0}'",
                rejectMessage.RejectionReason));

            default:
              throw new ChainException(string.Format(
                "Received improper message '{0}' during handshake session.",
                Command));
          }
        }
      }

      async Task SendMessage(NetworkMessage message)
      {
        NetworkStream.Write(MagicBytes, 0, MagicBytes.Length);

        byte[] command = Encoding.ASCII.GetBytes(
          message.Command.PadRight(CommandSize, '\0'));

        NetworkStream.Write(command, 0, command.Length);

        byte[] payloadLength = BitConverter.GetBytes(message.Payload.Length);
        NetworkStream.Write(payloadLength, 0, payloadLength.Length);

        byte[] checksum = CreateChecksum(
          message.Payload,
          message.Payload.Length);
        NetworkStream.Write(checksum, 0, checksum.Length);

        await NetworkStream.WriteAsync(
          message.Payload,
          0,
          message.Payload.Length)
          .ConfigureAwait(false);
      }

      byte[] CreateChecksum(byte[] payload, int count)
      {
        byte[] hash = SHA256.ComputeHash(
          SHA256.ComputeHash(payload, 0, count));

        return hash.Take(ChecksumSize).ToArray();
      }



      async Task ReadMessage(
        CancellationToken cancellationToken)
      {
        byte[] magicByte = new byte[1];

        for (int i = 0; i < MagicBytes.Length; i++)
        {
          await ReadBytes(
           magicByte,
           1,
           cancellationToken);

          if (MagicBytes[i] != magicByte[0])
          {
            i = magicByte[0] == MagicBytes[0] ? 0 : -1;
          }
        }

        await ReadBytes(
          MeassageHeader,
          MeassageHeader.Length,
          cancellationToken);

        Command = Encoding.ASCII.GetString(
          MeassageHeader.Take(CommandSize)
          .ToArray()).TrimEnd('\0');

        PayloadLength = BitConverter.ToInt32(
          MeassageHeader,
          CommandSize);

        if (PayloadLength > SIZE_MESSAGE_PAYLOAD_BUFFER)
        {
          throw new ChainException(string.Format(
            "Message payload too big exceeding {0} bytes.",
            SIZE_MESSAGE_PAYLOAD_BUFFER));
        }

        await ReadBytes(
          Payload,
          PayloadLength,
          cancellationToken);

        uint checksumMessage = BitConverter.ToUInt32(
          MeassageHeader, CommandSize + LengthSize);

        uint checksumCalculated = BitConverter.ToUInt32(
          CreateChecksum(
            Payload,
            PayloadLength),
          0);

        if (checksumMessage != checksumCalculated)
        {
          throw new ChainException("Invalid Message checksum.");
        }
      }

      async Task ReadBytes(
        byte[] buffer,
        int bytesToRead,
        CancellationToken cancellationToken)
      {
        int offset = 0;

        while (bytesToRead > 0)
        {
          int chunkSize = await NetworkStream.ReadAsync(
            buffer,
            offset,
            bytesToRead,
            cancellationToken)
            .ConfigureAwait(false);

          if (chunkSize == 0)
          {
            throw new ChainException(
              "Stream returns 0 bytes signifying end of stream.");
          }

          offset += chunkSize;
          bytesToRead -= chunkSize;
        }
      }


      
      public async Task StartMessageListener()
      {
        try
        {
          while (true)
          {
            await ReadMessage(default);

            switch (Command)
            {
              case "ping":
                await SendMessage(new PongMessage(
                  BitConverter.ToUInt64(Payload, 0)));
                break;

              case "addr":
                AddressMessage addressMessage =
                  new AddressMessage(Payload);
                break;

              case "sendheaders":
                await SendMessage(new SendHeadersMessage());

                break;

              case "feefilter":
                FeeFilterMessage feeFilterMessage =
                  new FeeFilterMessage(Payload);
                FeeFilterValue = feeFilterMessage.FeeFilterValue;
                break;

              default:
                lock (LOCK_IsExpectingMessageResponse)
                {
                  if (IsExpectingMessageResponse)
                  {
                    MessageResponseReady.Post(true);
                    return;
                  }
                }

                switch (Command)
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

                    string.Format(
                      "{0}: Recived headers message",
                      Command).Log(LogFile);

                    //await ReceiveHeader(
                    //  Payload,
                    //  this);

                    break;


                  default:
                    break;
                }
                break;
            }
          }
        }
        catch (Exception ex)
        {
          FlagDispose = true;

          string.Format(
           "Peer {0} experienced error " +
           "in message listener: \n{1}",
           GetID(),
           ex.Message)
           .Log(LogFile);
        }
      }
      

      
      public async Task<Header> GetHeaders(Header locator)
      {
        return await GetHeaders(
          new List<Header>() { locator });
      }

      public async Task<Header> GetHeaders(List<Header> locator)
      {
        string.Format(
          "Send getheader to peer {0}, \n" +
          "locator: {1} ... {2}",
          GetID(),
          locator.First().Hash.ToHexString(),
          locator.Count > 1 ? locator.Last().Hash.ToHexString() : "")
          .Log(LogFile);

        await SendMessage(new GetHeadersMessage(
          locator,
          ProtocolVersion));

        int timeout = TIMEOUT_GETHEADERS_MILLISECONDS;
        CancellationTokenSource cancellation =
          new CancellationTokenSource(timeout);

        lock (LOCK_IsExpectingMessageResponse)
        {
          IsExpectingMessageResponse = true;
        }

        while (await MessageResponseReady
            .ReceiveAsync(cancellation.Token) &&
            Command != "headers")
        {
          string.Format(
            "{0}: Received unexpected response message {1}",
            GetID(),
            Command)
            .Log(LogFile);

          StartMessageListener();
        }

        int indexPayload = 0;

        int countHeaders = VarInt.GetInt32(
          Payload, 
          ref indexPayload);

        if (countHeaders > 0)
        {
          BlockParser.Parse(
            Payload,
            PayloadLength,
            indexPayload);

          Header headerLocatorAncestor = locator
            .Find(h => h.Hash.IsEqual(
              BlockParser.HeaderRoot.HashPrevious));

          if (headerLocatorAncestor == null)
          {
            throw new ChainException(
              "GetHeaders does not connect to locator.");
          }

          BlockParser.HeaderRoot.HeaderPrevious =
            headerLocatorAncestor;
        }

        string.Format(
          "{0}: Received headers message, count = {1}",
          GetID(),
          countHeaders).Log(LogFile);

        lock (LOCK_IsExpectingMessageResponse)
        {
          IsExpectingMessageResponse = false;
        }

        StartMessageListener();

        return BlockParser.HeaderRoot;
      }


      public async Task<double> BuildHeaderchain(
        Header header,
        int height)
      {
        string.Format(
          "{0}: Build headerchain from header: \n{1}",
          GetID(),
          header.Hash.ToHexString(),
          height)
          .Log(LogFile);

        double difficulty = 0.0;

        while (true)
        {
          Blockchain.ValidateHeader(header, height);

          difficulty += header.Difficulty;

          if (header.HeaderNext == null)
          {
            if(height < 200000)
            {
              header.HeaderNext = await GetHeaders(header);
            }

            if (header.HeaderNext == null)
            {
              string.Format(
                "Height header chain {0}\n",
                height)
                .Log(LogFile);

              return difficulty;
            }
          }

          header = header.HeaderNext;
          height += 1;
        }
      }



      public async Task<Header> SkipDuplicates(
        Header header, List<Header> locator)
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



      List<Inventory> CreateInventories()
      {
        var inventories = new List<Inventory>();

        Header header = BlockParser.HeaderRoot;
        
        while(true)
        {
          inventories.Add(
            new Inventory(
              InventoryType.MSG_BLOCK,
              header.Hash));

          if(header == BlockParser.HeaderTip)
          {
            return inventories;
          }

          header = header.HeaderNext;
        }
      }


      public async Task DownloadBlocks(
        bool flagContinueDownload)
      {
        StopwatchDownload.Restart();

        var cancellation = new CancellationTokenSource(
            TIMEOUT_BLOCKDOWNLOAD_MILLISECONDS);

        try
        {
          if (!flagContinueDownload)
          {
            await SendMessage(new GetDataMessage(
              CreateInventories()));

            lock (LOCK_IsExpectingMessageResponse)
            {
              IsExpectingMessageResponse = true;
            }
          }

          Header header = BlockParser.HeaderRoot;

          while (true)
          {
            if (flagContinueDownload)
            {
              flagContinueDownload = false;
            }
            else
            {
              await MessageResponseReady
                .ReceiveAsync(cancellation.Token)
                .ConfigureAwait(false);

              if (Command == "notfound")
              {
                string.Format(
                  "{0}: Did not not find block in blockArchive {1}",
                  GetID(),
                  BlockParser.Index)
                  .Log(LogFile);

                FlagDispose = IsSyncMaster;
                return;
              }

              if (Command != "block")
              {
                string.Format(
                  "{0}: Received unexpected response message {1}",
                  GetID(),
                  Command)
                  .Log(LogFile);

                if (Command == "inv")
                {
                  var invMessage = new InvMessage(Payload);
                  if (invMessage.Inventories.First().IsTX())
                  {
                    throw new ChainException(
                      "Received TX inv message despite TX-disable signaled.");
                  }
                }

                StartMessageListener();
                continue;
              }
            }
                        
            BlockParser.ParsePayload(
              Payload,
              PayloadLength,
              header);

            if(BlockParser.IsArchiveBufferOverflow)
            {
              break;
            }

            if (BlockParser.HeaderTip == header)
            {
              lock (LOCK_IsExpectingMessageResponse)
              {
                IsExpectingMessageResponse = false;
              }

              StopwatchDownload.Stop();

              StartMessageListener();
              break;
            }

            header = header.HeaderNext;

            cancellation.CancelAfter(
              TIMEOUT_BLOCKDOWNLOAD_MILLISECONDS);

            StartMessageListener();
          }
        }
        catch (Exception ex)
        {
          string.Format(
            "Exception {0} in download of blockArchive {1}: \n{2}.",
            ex.GetType().Name,
            BlockParser.Index,
            ex.Message).Log(LogFile);

          BlockParser.IsInvalid = true;
          FlagDispose = true;

          return;
        }

        AdjustCountBlocksLoad();

        string.Format(
          "{0}: Downloaded {1} blocks in blockParser {2} in {3} ms.",
          GetID(),
          BlockParser.Height,
          BlockParser.Index,
          StopwatchDownload.ElapsedMilliseconds)
          .Log(LogFile);
      }

      void AdjustCountBlocksLoad()
      {
        var ratio = TIMEOUT_BLOCKDOWNLOAD_MILLISECONDS /
          (double)StopwatchDownload.ElapsedMilliseconds - 1;

        int correctionTerm = (int)(CountBlocksLoad * ratio);

        if(correctionTerm > 10)
        {
          correctionTerm = 10;
        }

        CountBlocksLoad = Math.Min(
          CountBlocksLoad + correctionTerm,
          500);

        CountBlocksLoad = Math.Max(CountBlocksLoad, 1);
      }
      


      //async Task ReceiveHeader(byte[] headerBytes, Peer peer)
      //{
      //  UTXOTable.BlockArchive blockArchive = null;
      //  LoadBlockArchive(ref blockArchive);

      //  blockArchive.Parse(headerBytes, 0, headerBytes.Length);

      //  int countLockTriesRemaining = 20;
      //  while (true)
      //  {
      //    lock (LOCK_IsBlockchainLocked)
      //    {
      //      if (IsBlockchainLocked)
      //      {
      //        if (countLockTriesRemaining == 0)
      //        {
      //          Console.WriteLine("Server overloaded.");
      //          return;
      //        }

      //        countLockTriesRemaining -= 1;
      //      }
      //      else
      //      {
      //        IsBlockchainLocked = true;
      //        break;
      //      }
      //    }

      //    await Task.Delay(250);
      //  }

      //  Header header = blockArchive.HeaderRoot;

      //  if (ContainsHeader(header.Hash))
      //  {
      //    Header headerContained = HeaderTip;

      //    var headerDuplicates = new List<byte[]>();
      //    int depthDuplicateAcceptedMax = 3;
      //    int depthDuplicate = 0;

      //    while (depthDuplicate < depthDuplicateAcceptedMax)
      //    {
      //      if (headerContained.Hash.IsEqual(header.Hash))
      //      {
      //        if (headerDuplicates.Any(h => h.IsEqual(header.Hash)))
      //        {
      //          throw new ChainException(
      //            string.Format(
      //              "Received duplicate header {0} more than once.",
      //              header.Hash.ToHexString()));
      //        }

      //        headerDuplicates.Add(header.Hash);
      //        if (headerDuplicates.Count > depthDuplicateAcceptedMax)
      //        {
      //          headerDuplicates = headerDuplicates.Skip(1)
      //            .ToList();
      //        }

      //        break;
      //      }

      //      if (headerContained.HeaderPrevious != null)
      //      {
      //        break;
      //      }

      //      headerContained = header.HeaderPrevious;
      //      depthDuplicate += 1;
      //    }

      //    if (depthDuplicate == depthDuplicateAcceptedMax)
      //    {
      //      throw new ChainException(
      //        string.Format(
      //          "Received duplicate header {0} with depth greater than {1}.",
      //          header.Hash.ToHexString(),
      //          depthDuplicateAcceptedMax));
      //    }
      //  }
      //  else if (header.HashPrevious.IsEqual(HeaderTip.Hash))
      //  {
      //    ValidateHeader(header, Height + 1);

      //    if (!await peer.TryDownloadBlocks())
      //    {
      //      return;
      //    }

      //    blockArchive.Index = IndexBlockArchive;

      //    UTXOTable.InsertBlockArchive(blockArchive);

      //    InsertHeaders(blockArchive);

      //    ArchiveBlock(blockArchive, UTXOIMAGE_INTERVAL_LISTEN);
      //  }
      //  else
      //  {
      //    await SynchronizeWithPeer(peer);

      //    peer.IsSynchronized = true;

      //    if (!ContainsHeader(header.Hash))
      //    {
      //      throw new ChainException(
      //        string.Format(
      //          "Advertized header {0} could not be inserted.",
      //          header.Hash.ToHexString()));
      //    }
      //  }

      //  IsBlockchainLocked = false;
      //}
      
      
      
      public async Task SendHeaders(List<Header> headers)
      {
        await SendMessage(new HeadersMessage(headers));
      }


      public bool IsInbound()
      {
        return Connection == ConnectionType.INBOUND;
      }


      public string GetID()
      {
        return IPAddress.ToString();
      }

      public void Dispose()
      {
        TcpClient.Dispose();

        LogFile.Dispose();

        DirectoryLogPeersDisposed = Directory.CreateDirectory(
          Path.Combine(
            DirectoryLogPeers.FullName,
            "disposed"));

        File.Move(Path.Combine(
          DirectoryLogPeers.FullName,
          GetID()),
          Path.Combine(
          DirectoryLogPeersDisposed.FullName,
          GetID()));
      }
    }
  }
}
