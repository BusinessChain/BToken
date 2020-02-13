using System;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Threading.Tasks.Dataflow;
using System.Diagnostics;

using BToken.Networking;


namespace BToken.Chaining
{
  partial class Blockchain
  {
    public const int COUNT_PEERS_MAX = 4;

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



    
    object LOCK_Peers = new object();
    List<BlockchainPeer> Peers = new List<BlockchainPeer>();

    public async Task Start()
    {
      await HeaderArchive.Load(
        Headerchain.GenesisHeader.HeaderHash);
      
      await UTXOArchive.Load(
        UTXOTable.Header.HeaderHash);

      while (true)
      {
        bool flagAddPeer = false;

        lock (LOCK_Peers)
        {
          Peers.RemoveAll(p => p.IsDisposed());

          if (
            Peers.Count <
            (COUNT_PEERS_MAX - (IsAnyPeerSynchronizing ? 1 : 0)))
          {
            flagAddPeer = true;
          }
        }

        if (flagAddPeer)
        {
          var peer = new BlockchainPeer(
            await Network.DispatchPeerOutbound());

          lock (LOCK_Peers)
          {
            Peers.Add(peer);
          }
        }

        BlockchainPeer peerSynchronizing;

        if (!IsAnyPeerSynchronizing)
        {
          lock (LOCK_Peers)
          {
            peerSynchronizing = Peers.Find(p => !p.IsSynchronized);

            if (peerSynchronizing != null)
            {
              Peers.Remove(peerSynchronizing);
            }
          }

          if (peerSynchronizing != null)
          {
            IsAnyPeerSynchronizing = true;
            SynchronizePeer(peerSynchronizing);
          }
        }

        await Task.Delay(5000);
      }
    }

    bool IsAnyPeerSynchronizing;


    async Task SynchronizePeer(BlockchainPeer peerSynchronizing)
    {
      try
      {
        Header headerBranch = await CreateHeaderBranch(peerSynchronizing);

        if (headerBranch != null)
        {
          if (IsFork(headerBranch))
          {
            Header headerFork =
              Headerchain.StageFork(ref headerBranch);

            UTXOTable.BackupToDisk();

            if (!TryRollBackUTXO(headerFork.HashPrevious))
            {
              // Reset everything, prepare for reindexing
              // If possible, try to use the UTXOImage

              throw new NotImplementedException();
            }

            if (await UTXOSynchronization(
              peerSynchronizing,
              headerFork,
              insertHeaders: false))
            {
              Headerchain.CommitFork();
            }
            else
            {
              UTXOTable.RestoreFromDisk();

              peerSynchronizing.ReportInvalid();

              lock (LOCK_Peers)
              {
                IsAnyPeerSynchronizing = false;
              }

              return;
            }
          }

          await UTXOSynchronization(
            peerSynchronizing,
            headerBranch,
            insertHeaders: true);
        }

        peerSynchronizing.IsSynchronized = true;

        lock (LOCK_Peers)
        {
          Peers.Add(peerSynchronizing);
          IsAnyPeerSynchronizing = false;
        }
      }
      catch (Exception ex)
      {
        Console.WriteLine(
          string.Format("Exception {0}: {1} when syncing with peer {2}",
          ex.GetType(),
          ex.Message,
          peerSynchronizing.GetIdentification()));

        peerSynchronizing.ReportInvalid();

        lock (LOCK_Peers)
        {
          IsAnyPeerSynchronizing = false;
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

    bool IsFork(Header headerBranch) => 
      !headerBranch.HashPrevious.IsEqual(
        Headerchain.HeaderTip.HeaderHash);

    bool TryRollBackUTXO(byte[] headerHash)
    {
      return true;
    }



    Header HeaderLoad;

    async Task<bool> UTXOSynchronization(
      BlockchainPeer peer,
      Header headerBranch,
      bool insertHeaders)
    {
      await UTXOArchive.Load(
        UTXOTable.Header.HeaderHash);

      HeaderLoad = headerBranch;

      Task[] tasksUTXOSyncSession = 
        new Task[COUNT_PEERS_MAX];
      
      tasksUTXOSyncSession[0] = RunUTXOSyncSession(peer);

      for (int i = 1; i < COUNT_PEERS_MAX; i += 1)
      {
        peer = new BlockchainPeer(
          await Network.DispatchPeerOutbound(default));

        tasksUTXOSyncSession[i] = RunUTXOSyncSession(peer);
      }

      await Task.WhenAll(tasksUTXOSyncSession);
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

    async Task RunUTXOSyncSession(BlockchainPeer peer)
    {
      Stopwatch stopwatchDownload = new Stopwatch();
      int countBlocks = COUNT_BLOCKS_DOWNLOADBATCH_INIT;
      DataBatch uTXOBatch;

      while (true)
      {
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
          // Was wenn alle downloads failen, 
          // wo wird der peer bestraft, wo der utxo restore
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
          }
          
          lock (LOCK_IsSynchronizationCompleted)
          {
            if (IsSynchronizationCompleted)
            {
              peer.Release();
              return;
            }
          }

          Task.Delay(100);
        }

        foreach (BlockContainer container in
          uTXOBatch.DataContainers)
        {
          container.Index = ArchiveIndexStore;

          try
          {
            // muss auf container ebene selbsheilend sein im falle von exception
            UTXOTable.InsertContainer(container);
          }
          catch (ChainException ex)
          {
            Console.WriteLine(
              "ChainException when inserting block {1}: \n{2}",
              container.Header.HeaderHash.ToHexString(),
              ex.Message);

            peer.ReportInvalid();

            lock (LOCK_IsSynchronizationCompleted)
            {
              IsSynchronizationCompleted = true;
            }
            
            if(Headerchain.IsForkStaged())
            {
              Headerchain.UnstageFork();
              UTXOTable.RestoreFromDisk();
            }

            //ArchiveContainers();

            return;
          }

          if (Headerchain.IsHeaderCommitFork(container.Header))
          {
            Headerchain.CommitFork();
          }

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
        
        if (uTXOBatch.IsCancellationBatch)
        {
          ArchiveContainers();

          lock (LOCK_IsSynchronizationCompleted)
          {
            IsSynchronizationCompleted = true;
          }

          peer.Release();

          return;
        }

        lock (LOCK_BatchIndex)
        {
          BatchIndex += 1;
        }

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
        if (HeaderLoad.HeaderNext == null)
        {
          return null;
        }

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

        uTXOBatch.IsCancellationBatch =
          HeaderLoad.HeaderNext == null;

        return uTXOBatch;
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
