using System;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Threading.Tasks.Dataflow;
using System.Diagnostics;
using System.Collections.Concurrent;

using BToken.Networking;



namespace BToken.Chaining
{
  partial class Blockchain
  {
    Headerchain Headerchain;
    DataArchiver HeaderArchive;
    
    UTXOTable UTXOTable;
    DataArchiver UTXOArchive;

    Network Network;

    Header GenesisHeader;


    public Blockchain(
      Header genesisHeader,
      byte[] genesisBlockBytes,
      List<HeaderLocation> checkpoints,
      Network network)
    {
      Headerchain = new Headerchain(
        genesisHeader,
        checkpoints);

      HeaderArchive = new DataArchiver(
        Headerchain,
        Path.Combine(
          AppDomain.CurrentDomain.BaseDirectory,
          "HeaderArchive"),
        50000,
        4);


      UTXOTable = new UTXOTable(
        genesisBlockBytes,
        Headerchain);

      UTXOArchive = new DataArchiver(
        UTXOTable,
        "J:\\BlockArchivePartitioned",
        50000,
        4);

      Network = network;
    }

    
    public async Task Start()
    {
      await HeaderArchive.Load(
        Headerchain.GenesisHeader.HeaderHash);
      
      await UTXOArchive.Load(
        UTXOTable.Header.HeaderHash);

      while (true)
      {
        var peer =
          new BlockchainPeer(
            await Network.DispatchPeerOutbound(default)
            .ConfigureAwait(false));

        try
        {
          int heightHeaderBranchRoot = await SynchronizeHeaderchain(peer);
          
          UTXOTable.RollBackToHeight(heightHeaderBranchRoot);

          if (UTXOTable.Header.HeaderNext != null)
          {
            await UTXOArchive.Load(
              UTXOTable.Header.HeaderHash);

            await SynchronizeUTXO(peer);
          }

          peer.Release();
          break;
        }
        catch (Exception ex)
        {
          Console.WriteLine(
            "{0} in SyncHeaderchainSession with peer {1}:\n {2}",
            ex.GetType().Name,
            peer == null ? "'null'" : peer.GetIdentification(),
            ex.Message);

          peer.Dispose();
        }
      }
    }



    Header HeaderBranchMain;

    async Task<int> SynchronizeHeaderchain(BlockchainPeer peer)
    {
      List<byte[]> headerLocator;
      Header headerBranch;

      lock (Headerchain.LOCK_IsChainLocked)
      {
        headerLocator =
          Headerchain.Locator.GetHeaderHashes().ToList();
      }

      var headerContainer = await peer
          .GetHeaders(headerLocator);

      if (headerContainer.CountItems == 0)
      {
        return Headerchain.Height;
      }

      headerBranch = headerContainer.HeaderRoot;

      int indexLocatorRoot = headerLocator.FindIndex(
        h => h.IsEqual(headerBranch.HashPrevious));

      if (indexLocatorRoot == -1)
      {
        peer.ReportInvalid();

        throw new ChainException(string.Format(
          "Received invalid response to getHeaders message:\n" +
          "Previous header {0} of headerBranch {1} not found in header locator.",
          headerBranch.HashPrevious.ToHexString(),
          headerBranch.HeaderHash.ToHexString()));
      }

      byte[] stopHash;
      if (indexLocatorRoot == headerLocator.Count - 1)
      {
        stopHash =
         ("00000000000000000000000000000000" +
         "00000000000000000000000000000000").ToBinary();
      }
      else
      {
        stopHash = headerLocator[indexLocatorRoot + 1];
      }

      while (
        Headerchain.TryReadHeader(
          headerBranch.HeaderHash,
          out Header header))
      {
        if (stopHash.IsEqual(headerBranch.HeaderHash))
        {
          peer.ReportInvalid();

          throw new ChainException(string.Format(
            "Received invalid response to getHeaders message:\n" +
            "Contains only duplicates."));
        }

        if (headerBranch.HeaderNext != null)
        {
          headerBranch = headerBranch.HeaderNext;
        }
        else
        {
          headerLocator = new List<byte[]>
              { headerBranch.HeaderHash };

          headerContainer = await peer.GetHeaders(headerLocator);

          if (headerContainer.CountItems == 0)
          {
            peer.ReportDuplicate();
            // send tip to channel
            return;
          }

          if (
            !headerContainer.HeaderRoot.HashPrevious
            .IsEqual(headerBranch.HeaderHash))
          {
            peer.ReportInvalid();
            return;
          }

          headerBranch = headerContainer.HeaderRoot;
        }
      }

      if (!Headerchain.TryReadHeader(
          headerBranch.HashPrevious,
          out Header headerBranchRoot))
      {
        peer.ReportInvalid();

        throw new ChainException(string.Format(
          "Received invalid response to getHeaders message:\n" +
          "Previous header {0} of headerBranch {1} not found in header locator.",
          headerBranch.HashPrevious.ToHexString(),
          headerBranch.HeaderHash.ToHexString()));
      }

      HeaderBranchMain = headerBranchRoot.HeaderNext;
      Header headerBranchTip = headerContainer.HeaderTip;
      
      while (true)
      {
        headerLocator = new List<byte[]> {
                headerBranchTip.HeaderHash };

        headerContainer = await peer.GetHeaders(headerLocator);

        if (headerContainer.CountItems == 0)
        {
          break;
        }

        if (
          !headerContainer.HeaderRoot.HashPrevious
          .IsEqual(headerBranchTip.HeaderHash))
        {
          peer.ReportInvalid();

          throw new ChainException(string.Format(
            "Received invalid response to getHeaders message:\n" +
            "Previous header {0} of headerContainer not equal to headerBranchTip {1}.",
            headerContainer.HeaderRoot.HashPrevious.ToHexString(),
            headerBranchTip.HeaderHash.ToHexString()));
        }

        headerBranchTip.HeaderNext = headerContainer.HeaderRoot;
        headerContainer.HeaderRoot.HeaderPrevious = headerBranchTip;
        headerBranchTip = headerContainer.HeaderTip;
      }

      Headerchain.InsertHeaderBranch(
        headerBranch, 
        out int heightHeaderBranchRoot);

      return heightHeaderBranchRoot;
    }


    Header HeaderLoad;
    BlockchainPeer PeerSynchronization;
    int CountPeersFailedSynchronizing;
    CancellationTokenSource CancellationSynchronizeUTXO;

    async Task SynchronizeUTXO(BlockchainPeer peer)
    {
      PeerSynchronization = peer;
      peer.Release();

      HeaderLoad = UTXOTable.Header;

      CancellationSynchronizeUTXO = new CancellationTokenSource();

      try
      {
        for (int i = 0; i < Network.PEERS_COUNT_OUTBOUND; i += 1)
        {
          peer = new BlockchainPeer(
            await Network.DispatchPeerOutbound(
              CancellationSynchronizeUTXO.Token));

          RunUTXOSyncSession(peer);
        }
      }
      catch (TaskCanceledException)
      {

      }
    }



    const int COUNT_BLOCKS_DOWNLOADBATCH_INIT = 2;
    public BufferBlock<DataBatch> QueueUTXOBatchInsertion =
      new BufferBlock<DataBatch>(
        new DataflowBlockOptions { BoundedCapacity = 10 });
    List<DataBatch> QueueBatchesCanceled = new List<DataBatch>();
    readonly object LOCK_QueueBatchesCanceled = new object();
    readonly object LOCK_BatchIndex = new object();
    int CountSessionsLoaded;
    readonly object LOCK_CountSessionsLoaded = new object();
    int BatchIndex;
    Dictionary<int, DataBatch> QueueDownloadBatch =
      new Dictionary<int, DataBatch>();

    async Task RunUTXOSyncSession(BlockchainPeer peer)
    {
      Stopwatch stopwatchDownload = new Stopwatch();
      int countBlocks = COUNT_BLOCKS_DOWNLOADBATCH_INIT;
      DataBatch uTXOBatch;


      try
      {
        while(true)
        {
        LoopLoadBatch:

          while (true)
          {
            lock(LOCK_LoadBatch)
            {
              uTXOBatch = LoadBatch(countBlocks);

              if(uTXOBatch != null)
              {
                CountSessionsLoaded += 1;
                break;
              }

              if (CountSessionsLoaded == 0)
              {
                peer.Release();
                return;
              }
            }

            await Task.Delay(1000);
          }

          stopwatchDownload.Restart();

          await peer.DownloadBlocks(uTXOBatch);

          stopwatchDownload.Stop();

          while(true)
          {
            lock (LOCK_LoadBatch)
            {
              if (uTXOBatch.Index == BatchIndex)
              {
                break;
              }

              if (QueueDownloadBatch.Count < 10)
              {
                QueueDownloadBatch.Add(uTXOBatch.Index, uTXOBatch);
                CountSessionsLoaded -= 1;
                goto LoopLoadBatch;
              }
            }

            Task.Delay(1000);
          }

          do
          {
            InsertBatch(uTXOBatch);

            lock (LOCK_LoadBatch)
            {
              BatchIndex += 1;
            }

            if (QueueDownloadBatch.TryGetValue(
              BatchIndex,
              out uTXOBatch))
            {
              QueueDownloadBatch.Remove(BatchIndex);
            }
            else
            {
              break;
            }
          } while (true);

          lock (LOCK_LoadBatch)
          {
            CountSessionsLoaded -= 1;
          }

          CalculateNewCountBlocks(
            ref countBlocks,
            stopwatchDownload.ElapsedMilliseconds);
        }
      }
      catch (Exception ex)
      {
        // If all channels fail, blame PeerSynchronizing and restore
        // Mainchain


        Console.WriteLine("Exception {0} in block download: \n{1}" +
          "batch {2} queued",
          ex.GetType().Name,
          ex.Message,
          uTXOBatch.Index);

        QueueBatchesCanceled.Add(uTXOBatch);

        peer.Dispose();

        countBlocks = COUNT_BLOCKS_DOWNLOADBATCH_INIT;
      }
    }
       


    int IndexLoad;
    readonly object LOCK_LoadBatch = new object();

    DataBatch LoadBatch(int countHeaders)
    {
      DataBatch uTXOBatch;

      lock (LOCK_QueueBatchesCanceled)
      {
        if (QueueBatchesCanceled.Any())
        {
          uTXOBatch = QueueBatchesCanceled.First();
          QueueBatchesCanceled.Remove(uTXOBatch);

          return uTXOBatch;
        }
      }

      if (HeaderLoad.HeaderNext != null)
      {
        uTXOBatch = new DataBatch(IndexLoad++);

        for (int i = 0; i < countHeaders; i += 1)
        {
          if (HeaderLoad.HeaderNext == null)
          {
            break;
          }

          HeaderLoad = HeaderLoad.HeaderNext;

          BlockContainer blockContainer =
            new BlockContainer(
              Headerchain,
              HeaderLoad);

          uTXOBatch.DataContainers.Add(blockContainer);
        }

        return uTXOBatch;
      }

      return null;
    }


    public void InsertBatch(DataBatch batch)
    {
      if (batch.IsCancellationBatch)
      {
        ArchiveContainers();
        return;
      }

      foreach (DataContainer container in
        batch.DataContainers)
      {
        container.Index = ArchiveIndexStore;

        InsertContainer(container);

        Containers.Add(container);
        CountItems += container.CountItems;

        if (CountItems >= SizeBatchArchive)
        {
          ArchiveContainers();

          Containers = new List<DataContainer>();
          CountItems = 0;

          ArchiveImage(ArchiveIndexStore);

          ArchiveIndexStore += 1;
        }
      }
    }

    const int TIMEOUT_BLOCKDOWNLOAD_MILLISECONDS = 20000;

    static void CalculateNewCountBlocks(
      ref int countBlocks,
      long elapsedMillisseconds)
    {
      const float safetyFactorTimeout = 10;
      const float marginFactorResetCountBlocksDownload = 5;

      float ratioTimeoutToDownloadTime = TIMEOUT_BLOCKDOWNLOAD_MILLISECONDS
        / (1 + elapsedMillisseconds);

      if (ratioTimeoutToDownloadTime > safetyFactorTimeout)
      {
        countBlocks += 1;
      }
      else if (ratioTimeoutToDownloadTime < marginFactorResetCountBlocksDownload)
      {
        countBlocks = COUNT_BLOCKS_DOWNLOADBATCH_INIT;
      }
      else if (countBlocks > 1)
      {
        countBlocks -= 1;
      }
    }


    public async Task InsertHeaders(
      byte[] headerBytes,
      BlockchainPeer channel)
    {
      var headerContainer = 
        new Headerchain.HeaderContainer(headerBytes);

      headerContainer.Parse();

      var headerBatch = new DataBatch();

      headerBatch.DataContainers.Add(headerContainer);
      
      await LockChain();

      if (Headerchain.TryReadHeader(
        headerContainer.HeaderRoot.HeaderHash, 
        out Header header))
      {
        if (UTXOTable
          .Synchronizer
          .MapBlockToArchiveIndex
          .ContainsKey(headerContainer.HeaderRoot.HeaderHash))
        {
          ReleaseChain();
          channel.ReportDuplicate();
          return;
        }
        else
        {
          // block runterladen
        }
      }
      else
      {
        try
        {
          byte[] stopHash =
           ("00000000000000000000000000000000" +
           "00000000000000000000000000000000").ToBinary();

          Headerchain.InsertHeaderBranchTentative(
            headerContainer.HeaderRoot,
            stopHash);
        }
        catch (ChainException ex)
        {
          switch (ex.ErrorCode)
          {
            case ErrorCode.ORPHAN:
              SynchronizeBlockchain(channel);
              return;

            case ErrorCode.INVALID:
              ReleaseChain();
              channel.ReportInvalid();
              return;
          }
        }
      }
      
      DataBatch blockBatch = 
        await channel.DownloadBlocks(headerBatch);

      try
      {
        UTXOTable.Synchronizer.InsertBatch(blockBatch);
      }
      catch (ChainException ex)
      {
        switch (ex.ErrorCode)
        {
          case ErrorCode.ORPHAN:
            // Block is not in Main chain
            break;

          case ErrorCode.INVALID:
            // Roll back inserted blocks
            // Restore UTXO by going to header tip 
            // (if there was no fork this doesn't do anything)
            ReleaseChain();
            channel.ReportInvalid();
            return;
        }
      }

      ReleaseChain();
    }
    

    public readonly object LOCK_IsChainLocked = new object();
    bool IsChainLocked;

    async Task LockChain()
    {
      while (true)
      {
        lock (LOCK_IsChainLocked)
        {
          if (!IsChainLocked)
          {
            IsChainLocked = true;
            return;
          }
        }

        await Task.Delay(200);
      }
    }

    void ReleaseChain()
    {
      lock (LOCK_IsChainLocked)
      {
        IsChainLocked = false;
      }
    }
  }
}
