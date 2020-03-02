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
          Peers.RemoveAll(p => p.IsStatusDisposed());

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
        Header headerBranch = await Headerchain
          .CreateHeaderBranch(peer);

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
    bool IsUTXOSynchronizationSuccess;
    bool FlagAllBatchesLoaded;

    async Task<bool> UTXOSynchronization(
      Header headerBranch,
      bool isHeaderBrancheStaged)
    {
      if(headerBranch == null)
      {
        return true;
      }

      IsUTXOSynchronizationSuccess = false;
      HeaderLoad = headerBranch;

      await UTXOArchive.Load(
        UTXOTable.Header.HeaderHash);
      
      while (true)
      {
        var peersIdle = new List<BlockchainPeer>();

        lock (LOCK_Peers)
        {
          if (Peers.All(p => p.IsStatusCompleted()))
          {
            return IsUTXOSynchronizationSuccess;
          }

          if (!FlagAllBatchesLoaded)
          {
            peersIdle = Peers.FindAll(p => p.IsStatusIdle());
          }
        }

        peersIdle.ForEach(p => p.SetStatusBusy());
        peersIdle.Select(p => RunUTXOSyncSession(p))
          .ToList();

        await Task.Delay(1000);
      }
    }


    const int COUNT_BLOCKS_DOWNLOADBATCH_INIT = 2;
    readonly object LOCK_BatchIndex = new object();
    int BatchIndex;

    async Task RunUTXOSyncSession(BlockchainPeer peer)
    {      
      if (peer.UTXOBatchesDownloaded.Count == 0)
      {
        DataBatch uTXOBatch = LoadBatch(peer.CountBlocksLoad);

        if (uTXOBatch == null)
        {
          FlagAllBatchesLoaded = true;
          peer.SetStatusIdle();
          return;
        }

        while(true)
        {
          await peer.DownloadBlocks(uTXOBatch);
          
          if(uTXOBatch.CountDataContainerDownloaded > 0)
          {
            peer.UTXOBatchesDownloaded.Push(uTXOBatch);

            if (uTXOBatch.CountDataContainerDownloaded ==
              uTXOBatch.DataContainers.Count)
            {
              break;
            }
          }

          BlockchainPeer peerNew = null;

          while (peerNew == null)
          {
            lock (LOCK_Peers)
            {
              if (Peers.All(p => p.IsStatusCompleted()))
              {
                return;
              }

              peerNew =
                Peers.Find(p => p.IsStatusIdle()) ??
                Peers.Find(p => p.IsStatusAwaitingInsertion());

              if (peerNew != null)
              {
                peerNew.SetStatusBusy();
                break;
              }
            }

            await Task.Delay(1000);
          }

          if (uTXOBatch.CountDataContainerDownloaded == 0)
          {
            peer = peerNew;
            continue;
          }

          queuePeersSplitBatches.Add(peer);

          DataBatch uTXOBatchSplit = new DataBatch(uTXOBatch.Index);

          uTXOBatchSplit.DataContainers = uTXOBatch.DataContainers
            .Skip(uTXOBatch.CountDataContainerDownloaded)
            .ToList();

          uTXOBatch.DataContainers = uTXOBatch.DataContainers
            .Take(uTXOBatch.CountDataContainerDownloaded)
            .ToList();

          uTXOBatch = uTXOBatchSplit;
        }
      }

      DataBatch uTXOBatchPeek = peer.UTXOBatchesDownloaded.Peek();

      lock (LOCK_BatchIndex)
      {
        if (uTXOBatchPeek.Index != BatchIndex)
        {
          peer.SetStatusAwaitingInsertion();
          return;
        }
      }

      while(true)
      {
        QueuePeersUTXOInserter.Post(peer);

        BlockchainPeer nextPeer;

        lock (LOCK_Peers)
        {
          nextPeer = Peers.Find(p =>
          p.IsStatusAwaitingInsertion() &&
          p.UTXOBatchesDownloaded.Peek().Index == BatchIndex + 1);
        }

        lock (LOCK_BatchIndex)
        {
          BatchIndex += 1;
        }

        if (nextPeer == null)
        {
          return;
        }

        nextPeer.SetStatusBusy();
        peer = nextPeer;
      }
      
      //CalculateNewCountBlocks(
      //  ref countBlocks,
      //  stopwatchDownload.ElapsedMilliseconds);
    }



    BufferBlock<BlockchainPeer> QueuePeersUTXOInserter = 
      new BufferBlock<BlockchainPeer>();

    async Task RunUTXOBatchInserter()
    {
      while(true)
      {
        BlockchainPeer peer = await QueuePeersUTXOInserter
          .ReceiveAsync()
          .ConfigureAwait(false);

        DataBatch uTXOBatch = peer.UTXOBatchesDownloaded.Pop();

        for (
          int i = 0;
          i < uTXOBatch.CountDataContainerDownloaded;
          i += 1)
        {
          var blockContainer =
            (UTXOTable.BlockContainer)uTXOBatch.DataContainers[i];

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

            peer.Dispose();

            return;
          }
        }

        if (uTXOBatch.IsCancellationBatch)
        {
          //ArchiveContainers();

          IsUTXOSynchronizationSuccess = true;

          peer.SetStatusCompleted();

          return;
        }

        RunUTXOSyncSession(peer);
      }
    }


    //blockContainer.Index = ArchiveIndexStore;

    //Containers.Add(blockContainer);
    //CountItems += blockContainer.CountItems;

    //if (CountItems >= SizeBatchArchive)
    //{
    //  ArchiveContainers();

    //  Containers = new List<DataContainer>();
    //  CountItems = 0;

    //  ArchiveImage(ArchiveIndexStore);

    //  ArchiveIndexStore += 1;
    //}

    int IndexLoad;
    readonly object LOCK_LoadBatch = new object();

    DataBatch LoadBatch(int countHeaders)
    {
      lock (LOCK_LoadBatch)
      {
        if (HeaderLoad.HeaderNext == null)
        {
          return null;
        }

        var uTXOBatch = new DataBatch(IndexLoad++);

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

        return uTXOBatch;
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
