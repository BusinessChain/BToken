using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using System.Threading.Tasks.Dataflow;
using System.Security.Cryptography;
using System.IO;

using BToken.Networking;



namespace BToken.Chaining
{
  public class BlockchainPeer
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
    Network.INetworkChannel NetworkPeer;

    public bool IsSynchronized;
    readonly object LOCK_Status = new object();
    StatusUTXOSyncSession Status;

    const int TIMEOUT_BLOCKDOWNLOAD_MILLISECONDS = 20000;
    const int TIMEOUT_GETHEADERS_MILLISECONDS = 5000;

    const int COUNT_BLOCKS_DOWNLOADBATCH_INIT = 2;
    Stopwatch StopwatchDownload = new Stopwatch();
    public int CountBlocksLoad = COUNT_BLOCKS_DOWNLOADBATCH_INIT;

    public Stack<UTXOTable.BlockArchive> BlockArchives =
      new Stack<UTXOTable.BlockArchive>();

    readonly object LOCK_IsExpectingMessageResponse = new object();
    bool IsExpectingMessageResponse;

    byte[] MessageBuffer;
    int IndexMessageBuffer;

    BufferBlock<NetworkMessage> MessageResponseBuffer = 
      new BufferBlock<NetworkMessage>();

    SHA256 SHA256 = SHA256.Create();




    public BlockchainPeer(
      Blockchain blockchain,
      Network.INetworkChannel networkChannel)
    {
      Blockchain = blockchain;
      NetworkPeer = networkChannel;
      
      Status = StatusUTXOSyncSession.IDLE;
    }


    public async Task StartListener()
    {      
      try
      {
        while (true)
        {
          NetworkMessage message = await ReceiveNetworkMessage(default);

          lock(LOCK_IsExpectingMessageResponse)
          {
            if (IsExpectingMessageResponse)
            {
              MessageResponseBuffer.Post(message);
              continue;
            }
          }

          switch (message.Command)
          {
            //case "ping":
            //  PingMessage pingMessage = new PingMessage(networkMessage);
            //  await NetworkMessageStreamer.WriteAsync(
            //    new PongMessage(pingMessage.Nonce)).ConfigureAwait(false);
            //  break;

            //case "addr":
            //  ProcessAddressMessage(message);
            //  break;

            //case "sendheaders":
            //  Task processSendHeadersMessageTask = ProcessSendHeadersMessageAsync(message);
            //  break;

            //case "feefilter":
            //  ProcessFeeFilterMessage(message);
            //  break;

            //case "getdata":
            //  var getDataMessage = new GetDataMessage(message);

            //  getDataMessage.Inventories.ForEach(inv =>
            //  {
            //    Console.WriteLine("getdata {0}: {1} from {2}",
            //      inv.Type,
            //      inv.Hash.ToHexString(),
            //      GetIdentification());
            //  });

            //  foreach (byte[] block in Blockchain.UTXOTable.Synchronizer.GetBlocks(
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
              var blockArchive =
                new UTXOTable.BlockArchive(message.Payload);

              blockArchive.Parse();
              
              await Blockchain.InsertHeaders(
                blockArchive, 
                this);
              
              break;


            default:
              break;
          }
        }
      }
      catch(Exception ex)
      {
        Dispose(string.Format(
          "Exception {0} when syncing: \n{1}",
          ex.GetType(),
          ex.Message));
      }
    }


    Stream Stream;

    string Command;
    uint PayloadLength;
    byte[] Payload;


    const int CommandSize = 12;
    const int LengthSize = 4;
    const int ChecksumSize = 4;

    const int HeaderSize = CommandSize + LengthSize + ChecksumSize;
    byte[] MessageHeaderBytes = new byte[HeaderSize];

    async Task<NetworkMessage> ReceiveNetworkMessage(
      CancellationToken cancellationToken)
    {
      await SyncStreamToMagic(cancellationToken);

      await ReadBytes(MessageHeaderBytes, cancellationToken);

      byte[] commandBytes = MessageHeaderBytes.Take(CommandSize).ToArray();
      Command = Encoding.ASCII.GetString(commandBytes).TrimEnd('\0');

      int payloadLength = BitConverter.ToInt32(MessageHeaderBytes, CommandSize);
      
      await ReadBytesToMessageBuffer(payloadLength, cancellationToken);

      uint checksumMessage = BitConverter.ToUInt32(MessageHeaderBytes, CommandSize + LengthSize);
      uint checksumCalculated = BitConverter.ToUInt32(CreateChecksum(Payload), 0);

      if (checksumMessage != checksumCalculated)
      {
        throw new NetworkException("Invalid Message checksum.");
      }

      return new NetworkMessage(Command, Payload);
    }

    const uint MagicValue = 0xF9BEB4D9;
    const uint MagicValueByteSize = 4;
    byte[] MagicBytes = new byte[MagicValueByteSize];

    async Task SyncStreamToMagic(CancellationToken cancellationToken)
    {
      byte[] singleByte = new byte[1];
      for (int i = 0; i < MagicBytes.Length; i++)
      {
        byte expectedByte = MagicBytes[i];

        await ReadBytes(singleByte, cancellationToken).ConfigureAwait(false);
        byte receivedByte = singleByte[0];
        if (expectedByte != receivedByte)
        {
          i = receivedByte == MagicBytes[0] ? 0 : -1;
        }
      }
    }

    async Task ReadBytesToMessageBuffer(
      int bytesToRead,
      CancellationToken cancellationToken)
    {
      while (bytesToRead > 0)
      {
        int chunkSize = await Stream.ReadAsync(
          MessageBuffer,
          IndexMessageBuffer,
          bytesToRead,
          cancellationToken).ConfigureAwait(false);

        if (chunkSize == 0)
        {
          throw new NetworkException("Stream returns 0 bytes signifying end of stream.");
        }

        IndexMessageBuffer += chunkSize;
        bytesToRead -= chunkSize;
      }
    }

    async Task ReadBytes(
      byte[] buffer, 
      CancellationToken cancellationToken)
    {
      int bytesToRead = buffer.Length;
      int offset = 0;

      while (bytesToRead > 0)
      {
        int chunkSize = await Stream.ReadAsync(
          buffer, 
          offset, 
          bytesToRead, 
          cancellationToken).ConfigureAwait(false);

        if (chunkSize == 0)
        {
          throw new NetworkException("Stream returns 0 bytes signifying end of stream.");
        }

        offset += chunkSize;
        bytesToRead -= chunkSize;
      }
    }

    byte[] CreateChecksum(byte[] payload)
    {
      byte[] hash = SHA256.ComputeHash(SHA256.ComputeHash(payload));
      return hash.Take(ChecksumSize).ToArray();
    }
    


    public async Task SendHeaders(List<Header> headers)
    {
      await NetworkPeer.SendMessage(
        new HeadersMessage(headers));
    }


    HeaderContainer HeaderContainer = new HeaderContainer();

    public async Task<UTXOTable.BlockArchive> GetHeaders(
      List<byte[]> locator)
    {
      await NetworkPeer.SendMessage(
        new GetHeadersMessage(locator));

      int timeout = TIMEOUT_GETHEADERS_MILLISECONDS;
      CancellationTokenSource cancellation =
        new CancellationTokenSource(timeout);

      lock (LOCK_IsExpectingMessageResponse)
      {
        IsExpectingMessageResponse = true;
      }
      
      while (true)
      {
        NetworkMessage networkMessage = await MessageResponseBuffer
          .ReceiveAsync(cancellation.Token)
          .ConfigureAwait(false);

        if (networkMessage.Command == "headers")
        {
          HeaderContainer.Buffer = networkMessage.Payload;
          break;
        }
      }

      lock (LOCK_IsExpectingMessageResponse)
      {
        IsExpectingMessageResponse = false;
      }

      HeaderContainer.Parse(SHA256);

      locator = locator.ToList();

      int indexLocatorAncestor = locator.FindIndex(
        h => h.IsEqual(
          HeaderContainer.HeaderRoot.HashPrevious));

      if (indexLocatorAncestor == -1)
      {
        throw new ChainException(
          "In getHeaders message received headers do not root in locator.");
      }

      locator = locator
        .Skip(indexLocatorAncestor)
        .Take(2)
        .ToList();

      if (locator.Count > 1)
      {
        Header header = HeaderContainer.HeaderRoot;

        do
        {
          if (header.Hash.IsEqual(locator[1]))
          {
            throw new ChainException(
              "Received headers do root in locator more than once.");
          }

          header = header.HeaderNext;
        } while (header != null);
      }

      return HeaderContainer;
    }


    List<Inventory> Inventories = new List<Inventory>();

    public async Task<bool> TryDownloadBlocks(
      UTXOTable.BlockArchive blockArchive)
    {
      try
      {
        Inventories.Clear();
        Header header = blockArchive.HeaderRoot;
        do
        {
          Inventories.Add(new Inventory(
            InventoryType.MSG_BLOCK,
            header.Hash));

          header = header.HeaderNext;
        } while (header != null);
          
        
        var cancellation = new CancellationTokenSource(
            TIMEOUT_BLOCKDOWNLOAD_MILLISECONDS);

        await NetworkPeer.SendMessage(
          new GetDataMessage(Inventories));

        lock (LOCK_IsExpectingMessageResponse)
        {
          IsExpectingMessageResponse = true;
        }

        MessageBuffer = blockArchive.Buffer;
        int startIndex = 0;

        header = blockArchive.HeaderRoot;

        while (header != null)
        {
          NetworkMessage networkMessage = await MessageResponseBuffer
            .ReceiveAsync(cancellation.Token)
            .ConfigureAwait(false);

          switch(networkMessage.Command)
          {
            case "notfound":
              SetStatusCompleted();
              return false;

            case "block":
              blockArchive.Parse(startIndex, header.MerkleRoot);
              startIndex = IndexMessageBuffer;
              header = header.HeaderNext;
              break;

            default:
              IndexMessageBuffer = startIndex;
              break;
          }
        }

        lock (LOCK_IsExpectingMessageResponse)
        {
          IsExpectingMessageResponse = false;
        }
      }
      catch (Exception ex)
      {
        Console.WriteLine(
          "Exception {0} in download of uTXOBatch {1}: \n{2}",
          ex.GetType().Name,
          blockArchive.Index,
          ex.Message);

        Dispose();

        return false;
      }

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

    public void ReportDuplicateHeader(byte[] headerHash)
    {
      if(HeaderDuplicates.Any(h => h.IsEqual(headerHash)))
      {
        throw new ChainException(
          string.Format(
            "Received duplicate header {0} more than once.",
            headerHash.ToHexString()));
      }

      HeaderDuplicates.Add(headerHash);
      if(HeaderDuplicates.Count > 3)
      {
        HeaderDuplicates = HeaderDuplicates.Skip(1)
          .ToList();
      }

    }

    public bool IsInbound()
    {
      return NetworkPeer.IsInbound();
    }

    public string GetIdentification()
    {
      return NetworkPeer.GetIdentification();
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
      NetworkPeer.Dispose();

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
