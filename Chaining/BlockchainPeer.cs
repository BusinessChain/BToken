﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using System.Threading.Tasks.Dataflow;
using System.Security.Cryptography;

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

    public Stack<DataBatch> UTXOBatchesDownloaded =
      new Stack<DataBatch>();

    BufferBlock<NetworkMessage> MessageBufferSynchronization = 
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
          NetworkMessage message = await NetworkPeer
            .ReceiveMessage(default)
            .ConfigureAwait(false);

          if(IsSynchronizing())
          {
            MessageBufferSynchronization.Post(message);
            continue;
          }

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
              var headerContainer =
                new HeaderContainer(message.Payload);

              headerContainer.Parse(SHA256);
              
              await Blockchain.InsertHeader(
                headerContainer, 
                this);
              
              break;


            default:
              break;
          }
        }
      }
      catch
      {
        Dispose();
      }
    }


    public async Task<HeaderContainer> GetHeaders(
      List<byte[]> locator)
    {
      await NetworkPeer.SendMessage(
        new GetHeadersMessage(locator));

      int timeout = TIMEOUT_GETHEADERS_MILLISECONDS;
      CancellationTokenSource cancellation =
        new CancellationTokenSource(timeout);

      byte[] headerBytes;
      while (true)
      {
        NetworkMessage networkMessage =
          await MessageBufferSynchronization.ReceiveAsync(
            cancellation.Token);

        if (networkMessage.Command == "headers")
        {
          headerBytes = networkMessage.Payload;
          break;
        }
      }

      var headerContainer =
        new HeaderContainer(headerBytes);

      headerContainer.Parse(SHA256);

      int indexLocatorAncestor = locator.FindIndex(
        h => h.IsEqual(headerContainer.HeaderRoot.HashPrevious));

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
        Header header = headerContainer.HeaderRoot;

        do
        {
          if (header.Hash.IsEqual(locator[1]))
          {
            throw new ChainException(
              "In getHeaders message, received headers root in locator more than once.");
          }

          header = header.HeaderNext;
        } while (header != null);
      }

      return headerContainer;
    }

    
    public async Task<bool> TryDownloadBlocks(
      DataBatch uTXOBatch)
    {
      uTXOBatch.CountItems = 0;

      try
      {
        List<Inventory> inventories = uTXOBatch.DataContainers
          .Select(container => new Inventory(
            InventoryType.MSG_BLOCK,
            ((UTXOTable.BlockContainer)container).Header.Hash))
          .ToList();

        await NetworkPeer.SendMessage(
          new GetDataMessage(inventories));

        var cancellationDownloadBlocks =
          new CancellationTokenSource(
            TIMEOUT_BLOCKDOWNLOAD_MILLISECONDS);

        foreach (UTXOTable.BlockContainer blockContainer in
          uTXOBatch.DataContainers)
        {
          while (true)
          {
            NetworkMessage networkMessage =
              await NetworkPeer
              .ReceiveMessage(cancellationDownloadBlocks.Token)
              .ConfigureAwait(false);

            if (networkMessage.Command == "notfound")
            {
              SetStatusCompleted();
              return false;
            }

            if (networkMessage.Command == "block")
            {
              blockContainer.Buffer = networkMessage.Payload;

              blockContainer.Parse();
              uTXOBatch.CountItems += blockContainer.CountItems;
              break;
            }
          }
        }
      }
      catch (Exception ex)
      {
        Console.WriteLine(
          "Exception {0} in download of uTXOBatch {1}: \n{2}",
          ex.GetType().Name,
          uTXOBatch.Index,
          ex.Message);

        Dispose();

        return false;
      }

      UTXOBatchesDownloaded.Push(uTXOBatch);

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




    public void ReportDuplicate()
    {
      // if number of duplicates exceeds number of headers
      // appended within the last hour, then dispose()
      throw new NotImplementedException();
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

    readonly object LOCK_FlagIsSynchronizing = new object();
    bool FlagIsSynchronizing;

    public bool IsSynchronizing()
    {
      lock (LOCK_FlagIsSynchronizing)
      {
        return FlagIsSynchronizing;
      }
    }
    public void SetIsSynchronizing()
    {
      lock (LOCK_FlagIsSynchronizing)
      {
        FlagIsSynchronizing = true;
      }
    }
    public void ClearIsSynchronizing()
    {
      lock (LOCK_FlagIsSynchronizing)
      {
        FlagIsSynchronizing = true;
      }
    }
  }
}
