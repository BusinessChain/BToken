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

      BlockchainPeer peerOld = null;

      for (int i = 0; i < Network.COUNT_PEERS_OUTBOUND; i += 1)
      {
        var peer =
          new BlockchainPeer(
            await Network.DispatchPeerOutbound(default)
            .ConfigureAwait(false));

        if(peerOld != null)
        {
          peerOld.Release();
          peerOld = null;
        }

        try
        {
          Header headerBranch = await CreateHeaderBranch(peer);

          if (headerBranch != null)
          {
            Headerchain.StageHeaderBranch(
              headerBranch,
              out int heightHeaderBranchRoot);

            await SynchronizeUTXO(
              peer,
              heightHeaderBranchRoot);

            Headerchain.CommitHeaderBranch();
          }

          peerOld = peer;
        }
        catch(Exception ex)
        {
          peer.ReportInvalid(
            string.Format("Exception {0}: {1}",
            ex.GetType(),
            ex.Message));
        }
      }
    }



    async Task<Header> CreateHeaderBranch(BlockchainPeer peer)
    {
      Header headerBranch = null;
      List<byte[]> headerLocator = Headerchain.GetLocator();

      var headerContainer = await peer.GetHeaders(headerLocator);

      if (headerContainer.CountItems == 0)
      {
        return headerBranch;
      }

      headerBranch = headerContainer.HeaderRoot;

      int indexLocatorRoot = headerLocator.FindIndex(
        h => h.IsEqual(headerBranch.HashPrevious));

      if (indexLocatorRoot == -1)
      {
        throw new ChainException(
          "Error in headerchain synchronization.");
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

      while (Headerchain.Contains(headerBranch.HeaderHash))
      {
        if (stopHash.IsEqual(headerBranch.HeaderHash))
        {
          throw new ChainException(
            "Error in headerchain synchronization.");
        }

        if (headerBranch.HeaderNext == null)
        {
          headerLocator = new List<byte[]>
              { headerBranch.HeaderHash };

          headerContainer = await peer.GetHeaders(headerLocator);

          if (headerContainer.CountItems == 0)
          {
            throw new ChainException(
              "Error in headerchain synchronization.");
          }

          if (!headerContainer.HeaderRoot.HashPrevious
            .IsEqual(headerBranch.HeaderHash))
          {
            throw new ChainException(
              "Error in headerchain synchronization.");
          }

          headerBranch = headerContainer.HeaderRoot;
        }
        else
        {
          headerBranch = headerBranch.HeaderNext;
        }
      }

      if (!Headerchain.Contains(headerBranch.HashPrevious))
      {
        throw new ChainException(
          "Error in headerchain synchronization.");
      }

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

        if (!headerContainer.HeaderRoot.HashPrevious
          .IsEqual(headerBranchTip.HeaderHash))
        {
          throw new ChainException(
            "Error in headerchain synchronization.");
        }

        headerBranchTip.HeaderNext = headerContainer.HeaderRoot;
        headerContainer.HeaderRoot.HeaderPrevious = headerBranchTip;
        headerBranchTip = headerContainer.HeaderTip;
      }

      return headerBranch;
    }

    int HeightHeaderBranchRoot;

    async Task SynchronizeHeaderchain(BlockchainPeer peer)
    {
      try
      {
        Headerchain.InsertHeaderBranch(
            headerBranch,
            out HeightHeaderBranchRoot);
      }
      catch (Exception ex)
      {
        Console.WriteLine(
          "{0} in SyncHeaderchainSession with peer {1}:\n {2}",
          ex.GetType().Name,
          peer == null ? "'null'" : peer.GetIdentification(),
          ex.Message);

        peer.Dispose();

        return false;
      }
      
      return true;
    }


    Header HeaderLoad;
    int CountPeersFailedSynchronizing;

    // Sobald branch stärker, wird per block gestaged, vorher wird alles gestaged
    // Es gibt also eine Staging area wo alles hineingestaged wird. 
    // Dann wird per Block oder seltener committed
    async Task<bool> SynchronizeUTXO(
      BlockchainPeer peer,
      int heightHeaderBranchRoot)
    {
      if(heightHeaderBranchRoot < UTXOTable.BlockHeight)
      {
        RollBackUTXO(heightHeaderBranchRoot);
      }

      await UTXOArchive.Load(
        UTXOTable.Header.HeaderHash);

      HeaderLoad = UTXOTable.Header;

      Task[] tasksUTXOSyncSession = 
        new Task[Network.COUNT_PEERS_OUTBOUND];

      tasksUTXOSyncSession[0] = RunUTXOSyncSession(peer);

      for (int i = 1; i < Network.COUNT_PEERS_OUTBOUND - 1; i += 1)
      {
        peer = new BlockchainPeer(
          await Network.DispatchPeerOutbound(default));

        tasksUTXOSyncSession[i] = RunUTXOSyncSession(peer);
      }

      await Task.WhenAll(tasksUTXOSyncSession);

      return IsSynchronizationUTXOSucceeded;
    }


    const int COUNT_BLOCKS_DOWNLOADBATCH_INIT = 2;
    public BufferBlock<DataBatch> QueueUTXOBatchInsertion =
      new BufferBlock<DataBatch>(
        new DataflowBlockOptions { BoundedCapacity = 10 });
    readonly object LOCK_QueueBatchesCanceled = new object();
    List<DataBatch> QueueBatchesCanceled = new List<DataBatch>();
    readonly object LOCK_BatchIndex = new object();
    int BatchIndex;
    readonly object LOCK_IsSynchronizationCompleted = new object();
    bool IsSynchronizationCompleted;
    bool IsSynchronizationUTXOSucceeded;
    Dictionary<int, DataBatch> QueueDownloadBatch =
      new Dictionary<int, DataBatch>();

    async Task RunUTXOSyncSession(BlockchainPeer peer)
    {
      Stopwatch stopwatchDownload = new Stopwatch();
      int countBlocks = COUNT_BLOCKS_DOWNLOADBATCH_INIT;
      DataBatch uTXOBatch;

      while (true)
      {
      LoopLoadBatch:

        while (true)
        {
          uTXOBatch = LoadBatch(countBlocks);

          if (uTXOBatch != null)
          {
            break;
          }

          lock (LOCK_IsSynchronizationCompleted)
          {
            if (IsSynchronizationCompleted)
            {
              peer.Release();
              return;
            }
          }

          await Task.Delay(1000);
        }
      
        try
        {
          stopwatchDownload.Restart();
          
          await peer.DownloadBlocks(uTXOBatch);

          stopwatchDownload.Stop();
        }
        catch (Exception ex)
        {
          Console.WriteLine(
            "Exception {0} in download of uTXOBatch {1}: \n{2}",
            ex.GetType().Name,
            uTXOBatch.Index,
            ex.Message);

          peer.Release();

          lock (LOCK_QueueBatchesCanceled)
          {
            QueueBatchesCanceled.Add(uTXOBatch);
          }

          return;
        }

        while (true)
        {
          lock (LOCK_BatchIndex)
          {
            if (uTXOBatch.Index == BatchIndex)
            {
              break;
            }

            if (QueueDownloadBatch.Count < 10)
            {
              QueueDownloadBatch.Add(uTXOBatch.Index, uTXOBatch);
              goto LoopLoadBatch;
            }
          }
          
          lock (LOCK_IsSynchronizationCompleted)
          {
            if (IsSynchronizationCompleted)
            {
              peer.Release();
              return;
            }
          }

          Task.Delay(1000);
        }

        do
        {
          try
          {
            InsertBatch(uTXOBatch);
          }
          catch (ChainException ex)
          {
            Console.WriteLine(
              "ChainException in download of uTXOBatch {0}: \n{1}",
              uTXOBatch.Index,
              ex.Message);

            lock(LOCK_IsSynchronizationCompleted)
            {
              IsSynchronizationCompleted = true;
            }

            peer.ReportInvalid();

            return;
          }

          if (uTXOBatch.IsCancellationBatch)
          {
            lock (LOCK_IsSynchronizationCompleted)
            {
              peer.Release();
              IsSynchronizationUTXOSucceeded = true;
              IsSynchronizationCompleted = true;
              return;
            }
          }

          lock (LOCK_BatchIndex)
          {
            BatchIndex += 1;

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
          }

        } while (true);
        
        CalculateNewCountBlocks(
          ref countBlocks,
          stopwatchDownload.ElapsedMilliseconds);
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

      lock (LOCK_LoadBatch)
      {
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
      }

      return null;
    }


    void InsertBatch(DataBatch batch)
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
