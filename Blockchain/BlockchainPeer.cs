using System;
using System.Collections.Generic;
using System.Linq;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

using BToken.Networking;



namespace BToken.Blockchain
{
  class BlockchainPeer
  {
    enum StatusUTXOSyncSession
    {
      IDLE,
      BUSY,
      AWAITING_INSERTION,
      COMPLETED,
      DISPOSED
    }

    Network.INetworkChannel NetworkPeer;

    public bool IsSynchronized;
    readonly object LOCK_Status = new object();
    StatusUTXOSyncSession Status;

    const int TIMEOUT_BLOCKDOWNLOAD_MILLISECONDS = 20000;
    const int TIMEOUT_GETHEADERS_MILLISECONDS = 5000;

    Stopwatch StopwatchDownload = new Stopwatch();
    public int CountBlocksLoad = 2;

    public DataBatch UTXOBatchDownloadNext;
    public Stack<DataBatch> UTXOBatchesDownloaded =
      new Stack<DataBatch>();




    public BlockchainPeer(
      Network.INetworkChannel networkChannel)
    {
      NetworkPeer = networkChannel;
    }


    public async Task<Headerchain.HeaderContainer> GetHeaders(
      IEnumerable<byte[]> locatorHashes)
    {
      await NetworkPeer.SendMessage(
        new GetHeadersMessage(locatorHashes));

      int timeout = TIMEOUT_GETHEADERS_MILLISECONDS;
      CancellationTokenSource cancellation = new CancellationTokenSource(timeout);

      byte[] headerBytes;
      while (true)
      {
        NetworkMessage networkMessage =
          await NetworkPeer.ReceiveMessage(cancellation.Token);

        if (networkMessage.Command == "headers")
        {
          headerBytes = networkMessage.Payload;
          break;
        }
      }

      var headerContainer =
        new Headerchain.HeaderContainer(headerBytes);

      headerContainer.Parse();

      return headerContainer;
    }


    public async Task DownloadBlocks(DataBatch uTXOBatch)
    {
      uTXOBatch.CountItems = 0;
      uTXOBatch.CountDataContainerDownloaded = 0;

      try
      {
        await RequestBlocks(
          uTXOBatch.DataContainers
          .Select(container => ((UTXOTable.BlockContainer)container)
          .Header.HeaderHash));
        
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
              return;
            }
            if (networkMessage.Command == "block")
            {
              blockContainer.Buffer = networkMessage.Payload;

              blockContainer.Parse();
              uTXOBatch.CountItems += blockContainer.CountItems;
              uTXOBatch.CountDataContainerDownloaded += 1;
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
      }
    }

    public async Task RequestBlocks(
      IEnumerable<byte[]> hashes)
    {
      List<Inventory> inventories = hashes
        .Select(h => new Inventory(
          InventoryType.MSG_BLOCK, h))
        .ToList();

      await NetworkPeer.SendMessage(
        new GetDataMessage(inventories));
    }


    public void ReportDuplicate()
    {
      throw new NotImplementedException();
    }
    public void ReportInvalid()
    {
      NetworkPeer.ReportInvalid();
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
  }
}
