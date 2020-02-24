using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
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
      COMPLETED,
      DISPOSED
    }

    Network.INetworkChannel NetworkPeer;

    public bool IsSynchronized;
    readonly object LOCK_Status = new object();
    StatusUTXOSyncSession Status;

    const int TIMEOUT_BLOCKDOWNLOAD_MILLISECONDS = 20000;
    const int TIMEOUT_GETHEADERS_MILLISECONDS = 5000;


    public DataBatch UTXOBatchDownloadNext;
    public List<DataBatch> UTXOBatchesDownloaded =
      new List<DataBatch>();




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


    public async Task DownloadBlocks()
    {
      try
      {
        await RequestBlocks(
          UTXOBatchDownloadNext.DataContainers
          .Select(container => ((UTXOTable.BlockContainer)container)
          .Header.HeaderHash));

        var cancellationDownloadBlocks =
          new CancellationTokenSource(TIMEOUT_BLOCKDOWNLOAD_MILLISECONDS);

        foreach (UTXOTable.BlockContainer blockContainer in
          UTXOBatchDownloadNext.DataContainers)
        {
          blockContainer.Buffer =
            await ReceiveBlock(cancellationDownloadBlocks.Token)
            .ConfigureAwait(false);

          blockContainer.Parse();
          UTXOBatchDownloadNext.CountItems += blockContainer.CountItems;
        }

        UTXOBatchesDownloaded.Add(UTXOBatchDownloadNext);
        UTXOBatchDownloadNext = null;
      }
      catch (Exception ex)
      {
        Console.WriteLine(
          "Exception {0} in download of uTXOBatch {1}: \n{2}",
          ex.GetType().Name,
          UTXOBatchDownloadNext.Index,
          ex.Message);

        throw ex;
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

    public async Task<byte[]> ReceiveBlock(
      CancellationToken cancellationToken)
    {
      while (true)
      {
        NetworkMessage networkMessage =
          await NetworkPeer
          .ReceiveMessage(cancellationToken)
          .ConfigureAwait(false);

        if (networkMessage.Command == "notfound")
        {
          return null;
        }
        if (networkMessage.Command == "block")
        {
          return networkMessage.Payload;
        }
      }
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


    public void SetCompleted()
    {
      lock (LOCK_Status)
      {
        Status = StatusUTXOSyncSession
          .COMPLETED;
      }
    }

    public bool IsCompleted()
    {
      lock (LOCK_Status)
      {
        return Status == StatusUTXOSyncSession
          .COMPLETED;
      }
    }

    public void Dispose()
    {
      // Der Blockchain peer soll grundsätzlich nicht
      // häufig reconnecten, daher führt einfach jedes Disposen
      // zu Blame.
      NetworkPeer.Blame();
      NetworkPeer.Dispose();

      lock (LOCK_Status)
      {
        Status = StatusUTXOSyncSession
          .DISPOSED;
      }
    }

    public bool IsDisposed()
    {
      lock (LOCK_Status)
      {
        return Status == StatusUTXOSyncSession
          .DISPOSED;
      }
    }

    public void SetBusy()
    {
      lock (LOCK_Status)
      {
        Status = StatusUTXOSyncSession
          .BUSY;
      }
    }

    public void SetIdle()
    {
      lock (LOCK_Status)
      {
        Status = StatusUTXOSyncSession
          .IDLE;
      }
    }
    public bool IsIdle()
    {
      lock (LOCK_Status)
      {
        return Status == StatusUTXOSyncSession
          .IDLE;
      }
    }
    public bool IsBusy()
    {
      lock (LOCK_Status)
      {
        return Status == StatusUTXOSyncSession
          .BUSY;
      }
    }
  }
}
