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
    partial class NetworkSynchronizer
    {
      partial class Peer
      {
        public enum RelayOptionFlags : byte
        {
          NoTxUntilFilter = 0x00,
          SendTxStandard = 0x01
        }
        public enum ServiceFlags : UInt64
        {
          // Nothing
          NODE_NONE = 0,
          // NODE_NETWORK means that the node is capable of serving the complete block chain. It is currently
          // set by all Bitcoin Core non pruned nodes, and is unset by SPV clients or other light clients.
          NODE_NETWORK = (1 << 0),
          // NODE_GETUTXO means the node is capable of responding to the getutxo protocol request.
          // Bitcoin Core does not support this but a patch set called Bitcoin XT does.
          // See BIP 64 for details on how this is implemented.
          NODE_GETUTXO = (1 << 1),
          // NODE_BLOOM means the node is capable and willing to handle bloom-filtered connections.
          // Bitcoin Core nodes used to support this by default, without advertising this bit,
          // but no longer do as of protocol version 70011 (= NO_BLOOM_VERSION)
          NODE_BLOOM = (1 << 2),
          // NODE_WITNESS indicates that a node can be asked for blocks and transactions including
          // witness data.
          NODE_WITNESS = (1 << 3),
          // NODE_XTHIN means the node supports Xtreme Thinblocks
          // If this is turned off then the node will not service nor make xthin requests
          NODE_XTHIN = (1 << 4),
          // NODE_NETWORK_LIMITED means the same as NODE_NETWORK with the limitation of only
          // serving the last 288 (2 day) blocks
          // See BIP159 for details on how this is implemented.
          NODE_NETWORK_LIMITED = (1 << 10),


          // Bits 24-31 are reserved for temporary experiments. Just pick a bit that
          // isn't getting used, or one not being used much, and notify the
          // bitcoin-development mailing list. Remember that service bits are just
          // unauthenticated advertisements, so your code must be robust against
          // collisions and other cases where nodes may be advertising a service they
          // do not actually support. Other service bits should be allocated via the
          // BIP process.
        }

        Blockchain Blockchain;

        public bool IsBusy;

        public bool FlagDispose;
        public bool IsSynchronized;

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
        public const string COMMAND_NOTFOUND = "notfound";

        const int SIZE_MESSAGE_PAYLOAD_BUFFER = 0x400000;
        byte[] Payload = new byte[SIZE_MESSAGE_PAYLOAD_BUFFER];
        int PayloadLength;

        const int HeaderSize = CommandSize + LengthSize + ChecksumSize;
        byte[] MeassageHeader = new byte[HeaderSize];
        byte[] MagicBytes = new byte[4] { 0xF9, 0xBE, 0xB4, 0xD9 };

        SHA256 SHA256 = SHA256.Create();

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
                        "{0}: Recived headers message.",
                        GetID())
                        .Log(LogFile);

                      if (!Blockchain.TryLock())
                      {
                        break;
                      }

                      StartMessageListener();

                      await ReceiveHeader();

                      Blockchain.ReleaseLock();

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
             "Exception {0} in message listener " +
             "of peer {1} experienced error: \n{2}",
             ex.GetType().Name,
             GetID(),
             ex.Message)
             .Log(LogFile);
          }
        }

        async Task ReceiveHeader()
        {
          BlockParser.Parse(
            Payload,
            PayloadLength,
            1);

          Header header = BlockParser.HeaderRoot;

          string.Format(
            "Received header {0}",
            header.Hash.ToHexString())
            .Log(LogFile);

          if (Blockchain.ContainsHeader(header.Hash))
          {
            Header headerContained = Blockchain.HeaderTip;

            var headerDuplicates = new List<byte[]>();
            int depthDuplicateAcceptedMax = 3;
            int depthDuplicate = 0;

            while (depthDuplicate < depthDuplicateAcceptedMax)
            {
              if (headerContained.Hash.IsEqual(header.Hash))
              {
                if (headerDuplicates.Any(h => h.IsEqual(header.Hash)))
                {
                  throw new ChainException(
                    string.Format(
                      "Received duplicate header {0} more than once.",
                      header.Hash.ToHexString()));
                }

                headerDuplicates.Add(header.Hash);
                if (headerDuplicates.Count > depthDuplicateAcceptedMax)
                {
                  headerDuplicates = headerDuplicates.Skip(1)
                    .ToList();
                }

                break;
              }

              if (headerContained.HeaderPrevious != null)
              {
                break;
              }

              headerContained = header.HeaderPrevious;
              depthDuplicate += 1;
            }

            if (depthDuplicate == depthDuplicateAcceptedMax)
            {
              throw new ChainException(
                string.Format(
                  "Received duplicate header {0} with depth greater than {1}.",
                  header.Hash.ToHexString(),
                  depthDuplicateAcceptedMax));
            }
          }
          else
          if (header.HashPrevious.IsEqual(
            Blockchain.HeaderTip.Hash))
          {
            header.HeaderPrevious = Blockchain.HeaderTip;

            Blockchain.ValidateHeaders(header);

            bool flagContinueDownload = false;

            while (true)
            {
              await DownloadBlocks(
                flagContinueDownload);

              if (
                FlagDispose ||
                Command == COMMAND_NOTFOUND)
              {
                return;
              }

              if (!Blockchain.TryArchiveBlocks(
                  BlockParser,
                  1))
              {
                FlagDispose = true;
                return;
              }

              BlockParser.ClearPayloadData();

              if (!BlockParser.IsArchiveBufferOverflow)
              {
                break;
              }

              BlockParser.RecoverFromOverflow();
              flagContinueDownload = true;
            }
          }
          else
          {
            IsSynchronized = false;
          }
        }



        public async Task<Header> GetHeaders(Header locator)
        {
          return await GetHeaders(
            new List<Header>() { locator });
        }

        public async Task<Header> GetHeaders(
          List<Header> locator)
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
          else
          {
            BlockParser.HeaderRoot = null;
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
          double difficulty = 0.0;

          while (true)
          {
            Blockchain.ValidateHeader(header, height);

            difficulty += header.Difficulty;

            if (header.HeaderNext == null)
            {
              if (height < 2000000)
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

                if (Command == COMMAND_NOTFOUND)
                {
                  string.Format(
                    "{0}: Did not not find block in blockArchive {1}",
                    GetID(),
                    BlockParser.Index)
                    .Log(LogFile);

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

              if (BlockParser.IsArchiveBufferOverflow)
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
              "{0} with peer {1} when downloading blockArchive {2}: \n{3}.",
              ex.GetType().Name,
              GetID(),
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

        List<Inventory> CreateInventories()
        {
          var inventories = new List<Inventory>();

          Header header = BlockParser.HeaderRoot;

          while (true)
          {
            inventories.Add(
              new Inventory(
                InventoryType.MSG_BLOCK,
                header.Hash));

            if (header == BlockParser.HeaderTip)
            {
              return inventories;
            }

            header = header.HeaderNext;
          }
        }

        void AdjustCountBlocksLoad()
        {
          var ratio = TIMEOUT_BLOCKDOWNLOAD_MILLISECONDS /
            (double)StopwatchDownload.ElapsedMilliseconds - 1;

          int correctionTerm = (int)(CountBlocksLoad * ratio);

          if (correctionTerm > 10)
          {
            correctionTerm = 10;
          }

          CountBlocksLoad = Math.Min(
            CountBlocksLoad + correctionTerm,
            500);

          CountBlocksLoad = Math.Max(CountBlocksLoad, 1);
        }



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
}
