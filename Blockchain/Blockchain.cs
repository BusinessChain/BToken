using System;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Threading.Tasks.Dataflow;
using System.Diagnostics;

using BToken.Networking;


namespace BToken.Blockchain
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
    object LOCK_IsAnyPeerSynchronizing = new object();
    bool IsAnyPeerSynchronizing;

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
            await Network.CreateNetworkPeer());

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
          }

          if (peerSynchronizing != null)
          {
            IsAnyPeerSynchronizing = true;

            SynchronizeWithPeer(peerSynchronizing);
          }
        }

        await Task.Delay(1000);
      }
    }

    bool IsFork(Header headerBranch) => 
      !headerBranch.HashPrevious.IsEqual(
        Headerchain.HeaderTip.HeaderHash);

    bool TryRollBackUTXO(byte[] headerHash)
    {
      return true;
    }


    async Task SynchronizeWithPeer(BlockchainPeer peer)
    {
      try
      {
        Header headerBranch = await Headerchain.CreateHeaderBranch(
          peer);

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
              headerFork,
              isHeaderBrancheStaged: true))
            {
              Headerchain.CommitFork();
            }
            else
            {
              UTXOTable.RestoreFromDisk();
              headerBranch = null;

              peer.Dispose();
            }
          }

          if (!await UTXOSynchronization(
            headerBranch,
            isHeaderBrancheStaged: false))
          {
            peer.Dispose();
          }
        }

        peer.IsSynchronized = true;
      }
      catch (Exception ex)
      {
        Console.WriteLine(
          string.Format("Exception {0}: {1} when syncing with peer {2}",
          ex.GetType(),
          ex.Message,
          peer.GetIdentification()));

        peer.Dispose();
      }

      IsAnyPeerSynchronizing = false;
    }

    Header HeaderLoad;
    readonly object LOCK_FlagUTXOSyncSessionsCompleted = new object();
    bool FlagUTXOSyncSessionsCompleted;
    readonly object LOCK_IsUTXOSyncSessionsSuccess = new object();
    bool IsUTXOSyncSessionsSuccess;

    async Task<bool> UTXOSynchronization(
      Header headerBranch,
      bool isHeaderBrancheStaged)
    {
      if(headerBranch == null)
      {
        return true;
      }

      IsUTXOSyncSessionsSuccess = false;
      HeaderLoad = headerBranch;

      await UTXOArchive.Load(
        UTXOTable.Header.HeaderHash);

      List<BlockchainPeer> peers;

      while (true)
      {
        lock (LOCK_Peers)
        {
          if(Peers.All(p => p.IsCompleted()))
          {
            Peers.ForEach(p => p.SetIdle());

            return IsUTXOSyncSessionsSuccess;
          }

          peers = Peers.Where(p => p.IsIdle())
            .ToList();
        }
        
        peers.ForEach(p => p.SetBusy());
        peers.Select(p => RunUTXOSyncSession(p)).ToArray();

        await Task.Delay(1000);
      }
    }


    const int COUNT_BLOCKS_DOWNLOADBATCH_INIT = 2;
    public BufferBlock<DataBatch> QueueUTXOBatchInsertion =
      new BufferBlock<DataBatch>(
        new DataflowBlockOptions { BoundedCapacity = 10 });
    readonly object LOCK_QueueBatchesCanceled = new object();
    List<DataBatch> QueueBatchesCanceled = new List<DataBatch>();
    readonly object LOCK_BatchIndex = new object();
    int BatchIndex;

    async Task RunUTXOSyncSession(BlockchainPeer peer)
    {
      Stopwatch stopwatchDownload = new Stopwatch();
      int countBlocks = COUNT_BLOCKS_DOWNLOADBATCH_INIT;
      DataBatch uTXOBatch = null;

      while (true)
      {
        while (true)
        {
          lock (LOCK_IsUTXOSyncSessionsSuccess)
          {
            if (IsUTXOSyncSessionsSuccess)
            {
              peer.SetCompleted();
              return;
            }
          }

          lock (LOCK_QueueBatchesCanceled)
          {
            if (QueueBatchesCanceled.Any())
            {
              uTXOBatch = QueueBatchesCanceled
                .Find(b => b.Index == QueueBatchesCanceled
                .Min(c => c.Index));

              QueueBatchesCanceled.Remove(uTXOBatch);
            }
          }

          if (uTXOBatch == null)
          {
            LoadBatch(
              countBlocks,
              ref uTXOBatch);
          }
          
          if (uTXOBatch != null)
          {
            peer.UTXOBatchDownloadNext = uTXOBatch;
            break;
          }

          await Task.Delay(1000);
        }

        try
        {
          await peer.DownloadBlocks();
        }
        catch
        {
          lock (LOCK_QueueBatchesCanceled)
          {
            QueueBatchesCanceled.Add(uTXOBatch);
          }

          peer.Dispose();

          return;
        }

        while (true)
        {
          lock (LOCK_BatchIndex)
          {
            uTXOBatch = peer.UTXOBatchesDownloaded
              .Find(b => b.Index == BatchIndex);
          }

          if (uTXOBatch != null)
          {
            peer.UTXOBatchesDownloaded.Remove(uTXOBatch);
            break;
          }

          // Wenn ein UTXOBatch canceled wird muss dort gerade auf einen
          // neuen peer gewartet und ihm der batch übergeben werden
          // in der Liste Peers kann ein peer gezogen werden wenn er Status Idle 
          // oder Awaiting_insertion hat. Es braucht kein BatchCanceledQueue.
          // Wenn Insertion schief läuft, werden alle Prozesse abgebrochen

          //lock
          QueuePeersWithDownladedBatches.Add(uTXO.Index, peer);
          peer.SetAwaitingInsertion();
          return;





          DataBatch batchCanceled = null;

          if(!peer.IsDisposed())
          {
            lock (LOCK_QueueBatchesCanceled)
            {
              if (QueueBatchesCanceled.Any())
              {
                int batchIndexCanceledMin = QueueBatchesCanceled
                  .Min(b => b.Index);

                if (batchIndexCanceledMin < peer.UTXOBatchesDownloaded
                  .Min(b => b.Index))
                {
                  batchCanceled = QueueBatchesCanceled
                    .Find(b => b.Index == batchIndexCanceledMin);

                  QueueBatchesCanceled.Remove(batchCanceled);

                  peer.UTXOBatchesDownloaded.Add(batchCanceled);
                }
              }
            }

            if (batchCanceled != null)
            {
              peer.UTXOBatchDownloadNext = batchCanceled;

              try
              {
                await peer.DownloadBlocks();
              }
              catch
              {
                lock (LOCK_QueueBatchesCanceled)
                {
                  QueueBatchesCanceled.Add(batchCanceled);
                }

                peer.Dispose();
              }

              continue;
            }
          }

          // Das wegbringen, peers mit batches in einem Dict speichern, 
          // das das ein peer immer den nächsten peer hohlen kann der dran ist.
          Task.Delay(100);
        }

        foreach (UTXOTable.BlockContainer blockContainer in
          uTXOBatch.DataContainers)
        {
          blockContainer.Index = ArchiveIndexStore;

          if(blockContainer.Buffer == null)
          {
            uTXOBatch.DataContainers = uTXOBatch.DataContainers.Skip(
              uTXOBatch.DataContainers.IndexOf(blockContainer))
              .ToList();

            lock (LOCK_QueueBatchesCanceled)
            {
              QueueBatchesCanceled.Add(uTXOBatch);
            }

            peer.SetCompleted();

            return;
          }

          try
          {
            UTXOTable.InsertBlockContainer(blockContainer);
          }
          catch (ChainException ex)
          {
            Console.WriteLine(
              "ChainException when inserting block {1}: \n{2}",
              blockContainer.Header.HeaderHash.ToHexString(),
              ex.Message);

            lock (LOCK_QueueBatchesCanceled)
            {
              QueueBatchesCanceled.Add(uTXOBatch);
            }

            peer.Dispose();

            //ArchiveContainers();

            return;
          }
          
          Containers.Add(blockContainer);
          CountItems += blockContainer.CountItems;

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
          
          lock (LOCK_IsUTXOSyncSessionsSuccess)
          {
            IsUTXOSyncSessionsSuccess = true;
          }

          peer.SetCompleted();

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

    void LoadBatch(int countHeaders, ref DataBatch uTXOBatch)
    {
      lock (LOCK_LoadBatch)
      {
        if (HeaderLoad.HeaderNext == null)
        {
          uTXOBatch = null;
        }

        uTXOBatch = new DataBatch(IndexLoad++);

        for (int i = 0; i < countHeaders; i += 1)
        {
          if (HeaderLoad.HeaderNext == null)
          {
            break;
          }

          HeaderLoad = HeaderLoad.HeaderNext;

          var blockContainer =
            new UTXOTable.BlockContainer(
              Headerchain,
              HeaderLoad);

          uTXOBatch.DataContainers.Add(blockContainer);
        }

        uTXOBatch.IsCancellationBatch =
          HeaderLoad.HeaderNext == null;
      }
    }

    
    
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
