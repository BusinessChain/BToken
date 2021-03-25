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
    partial class BlockchainNetwork
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

        const int TIMEOUT_BLOCKDOWNLOAD_MILLISECONDS = 5000;
        const int TIMEOUT_GETHEADERS_MILLISECONDS = 3000;

        const int COUNT_BLOCKS_DOWNLOADBATCH_INIT = 1;
        Stopwatch StopwatchDownload = new Stopwatch();
        public int CountBlocksLoad = COUNT_BLOCKS_DOWNLOADBATCH_INIT;

        public HeaderDownload HeaderDownload;
        public BlockDownload BlockDownload;

        public UTXOTable.BlockParser BlockParser =
          new UTXOTable.BlockParser();
        
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
        CancellationTokenSource Cancellation =
          new CancellationTokenSource();

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
            new CancellationTokenSource(TimeSpan.FromSeconds(5))
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

                  throw new ProtocolException(
                    "Remote peer rejected: " + rejectionReason);
                }

                await SendMessage(new VerAckMessage());
                break;

              case "reject":
                RejectMessage rejectMessage = new RejectMessage(Payload);

                throw new ProtocolException(
                  string.Format("Peer rejected handshake: '{0}'",
                  rejectMessage.RejectionReason));

              default:
                throw new ProtocolException(string.Format(
                  "Received improper message '{0}' during handshake session.",
                  Command));
            }
          }
        }

        async Task SendMessage(NetworkMessage message)
        {
          string.Format(
            "{0} Send message {1}",
            GetID(),
            message.Command)
            .Log(LogFile);

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
            throw new ProtocolException(string.Format(
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
            throw new ProtocolException("Invalid Message checksum.");
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
              throw new ProtocolException(
                "Stream returns 0 bytes signifying end of stream.");
            }

            offset += chunkSize;
            bytesToRead -= chunkSize;
          }
        }


        readonly object LOCK_StateProtocol = new object();
        enum StateProtocol
        {
          IDLE = 0,
          AwaitingBlock,
          AwaitingHeader
        }

        StateProtocol State;
        BufferBlock<bool> SignalProtocolTaskCompleted =
          new BufferBlock<bool>();

        public async Task StartMessageListener()
        {
          try
          {
            while (true)
            {
              await ReadMessage(Cancellation.Token);

              string.Format(
                "{0} received message {1}",
                GetID(),
                Command)
                .Log(LogFile);

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

                case "block":

                  byte[] blockBytes = Payload
                    .Take(PayloadLength)
                    .ToArray();

                  Block block = BlockParser.ParseBlock(blockBytes);
                  
                  string.Format(
                    "{0}: Receives block {1}.",
                    GetID(),
                    block.Header.Hash.ToHexString())
                    .Log(LogFile);

                  if (IsStateIdle())
                  {
                    // Received unsolicited block
                  }
                  else if(IsStateAwaitingBlock())
                  {
                    BlockDownload.InsertBlock(block);

                    if (BlockDownload.IsDownloadCompleted)
                    {
                      SignalProtocolTaskCompleted.Post(true);

                      Cancellation = new CancellationTokenSource();

                      break;
                    }

                    Cancellation.CancelAfter(
                      TIMEOUT_BLOCKDOWNLOAD_MILLISECONDS);
                  }

                  break;

                case "headers":

                  Header header = null;
                  int index = 0;

                  int countHeaders = VarInt.GetInt32(
                    Payload, 
                    ref index);

                  string.Format(
                    "{0}: Receiving {1} headers.",
                    GetID(),
                    countHeaders)
                    .Log(LogFile);

                  if (IsStateIdle())
                  {
                    header = BlockParser.ParseHeader(
                      Payload,
                      ref index);

                    index += 1;

                    if (!Blockchain.TryLock())
                    {
                      break;
                    }

                    try
                    {
                      string.Format(
                        "Received unsolicited header {0}",
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
                              throw new ProtocolException(
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
                          throw new ProtocolException(
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

                        await Blockchain.Network
                          .SynchronizeUTXO(header, this);

                        Blockchain.ReleaseLock();

                        break;
                      }
                      else
                      {
                        IsSynchronized = false;
                      }
                    }
                    catch (Exception ex)
                    {
                      Blockchain.ReleaseLock();

                      throw ex;
                    }

                    Blockchain.ReleaseLock();
                  }
                  else if(IsStateAwaitingHeader())
                  {
                    if(countHeaders > 0)
                    {
                      header = BlockParser.ParseHeader(
                        Payload,
                        ref index);

                      index += 1;

                      HeaderDownload.InsertHeader(header);

                      while (index < PayloadLength)
                      {
                        header = BlockParser.ParseHeader(
                          Payload,
                          ref index);

                        index += 1;

                        HeaderDownload.InsertHeader(header);
                      }
                    }

                    string.Format(
                      "{0}: Signal getheaders task complete.",
                      GetID())
                      .Log(LogFile);

                    SignalProtocolTaskCompleted.Post(true);

                    Cancellation = new CancellationTokenSource();

                    break;
                  }
                  
                  break;

                case "notfound":

                  Console.WriteLine(
                    "Command notfound not implemented yet.");

                  break;

                case "inv":

                  var invMessage = new InvMessage(Payload);
                  if (invMessage.Inventories.First().IsTX())
                  {
                    throw new ProtocolException(
                      "Received TX inv message despite TX-disable signaled.");
                  }

                  break;


                default:
                  // Send message unknown
                  break;
              }
            }
          }
          catch (Exception ex)
          {
            FlagDispose = true;

            Cancellation.Cancel();

            string.Format(
             "Peer {0} experienced error " +
             "in message listener: \n{1}",
             GetID(),
             "message: " + ex.Message + 
             "stack trace: " + ex.StackTrace)
             .Log(LogFile);
          }
        }
        
        
        async Task<Header> GetHeaders(Header header)
        {
          HeaderDownload.Locator.Clear();
          HeaderDownload.Locator.Add(header);

          return await GetHeaders();
        }

        public async Task<Header> GetHeaders()
        {
          string.Format(
            "Send getheaders to peer {0}, \n" +
            "locator: {1} ... {2}",
            GetID(),
            HeaderDownload.Locator.First().Hash.ToHexString(),
            HeaderDownload.Locator.Count > 1 ? HeaderDownload.Locator.Last().Hash.ToHexString() : "")
            .Log(LogFile);

          HeaderDownload.Reset();

          await SendMessage(new GetHeadersMessage(
            HeaderDownload.Locator,
            ProtocolVersion));

          lock (LOCK_StateProtocol)
          {
            State = StateProtocol.AwaitingHeader;
          }
          
          Cancellation.CancelAfter(
              TIMEOUT_GETHEADERS_MILLISECONDS);

          await SignalProtocolTaskCompleted
            .ReceiveAsync(Cancellation.Token)
            .ConfigureAwait(false);

          lock (LOCK_StateProtocol)
          {
            State = StateProtocol.IDLE;
          }

          return HeaderDownload.HeaderRoot;
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
              header.HeaderNext = await GetHeaders(header);

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
          Header header)
        {
          Header headerAncestor = header.HeaderPrevious;

          Header stopHeader = HeaderDownload.Locator[
            HeaderDownload.Locator.IndexOf(headerAncestor) + 1];

          while (headerAncestor.HeaderNext.Hash
            .IsEqual(header.Hash))
          {
            headerAncestor = headerAncestor.HeaderNext;

            if (headerAncestor == stopHeader)
            {
              throw new ProtocolException(
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

          try
          {
            if (flagContinueDownload)
            {
              flagContinueDownload = false;
            }
            else
            {
              List<Inventory> inventories =
                BlockDownload.HeadersExpected.Select(
                  h => new Inventory(
                    InventoryType.MSG_BLOCK,
                    h.Hash))
                    .ToList();

              await SendMessage(
                new GetDataMessage(inventories));
            }

            lock(LOCK_StateProtocol)
            {
              State = StateProtocol.AwaitingBlock;
            }

            Cancellation.CancelAfter(
                TIMEOUT_BLOCKDOWNLOAD_MILLISECONDS);

            await SignalProtocolTaskCompleted
              .ReceiveAsync(Cancellation.Token)
              .ConfigureAwait(false);

            lock (LOCK_StateProtocol)
            {
              State = StateProtocol.IDLE;
            }
          }
          catch (Exception ex)
          {
            string.Format(
              "{0} with peer {1} when downloading blockArchive {2}: \n{3}.",
              ex.GetType().Name,
              GetID(),
              BlockDownload.Index,
              ex.Message).Log(LogFile);

            FlagDispose = true;

            return;
          }

          AdjustCountBlocksLoad();

          string.Format(
            "{0}: Downloaded {1} blocks in download {2} in {3} ms.",
            GetID(),
            BlockDownload.Blocks.Count,
            BlockDownload.Index,
            StopwatchDownload.ElapsedMilliseconds)
            .Log(LogFile);
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

        bool IsStateIdle()
        {
          lock (LOCK_StateProtocol)
          {
            return State == StateProtocol.IDLE;
          }
        }

        bool IsStateAwaitingHeader()
        {
          lock (LOCK_StateProtocol)
          {
            return State == StateProtocol.AwaitingHeader;
          }
        }

        bool IsStateAwaitingBlock()
        {
          lock (LOCK_StateProtocol)
          {
            return State == StateProtocol.AwaitingBlock;
          }
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
          Console.WriteLine(
            "Dispose peer {0}.",
            GetID());

          Cancellation.Cancel();

          TcpClient.Dispose();

          LogFile.Dispose();

          File.Move(
            Path.Combine(
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
