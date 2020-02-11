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
    BlockchainPeer PeerSynchronizing;

    public async Task Start()
    {
      await HeaderArchive.Load(
        Headerchain.GenesisHeader.HeaderHash);
      
      await UTXOArchive.Load(
        UTXOTable.Header.HeaderHash);


      Parallel.For(
        0, Network.COUNT_PEERS_OUTBOUND,
        i => RunBlockchainPeer());

      var cancellationPeerLoader = new CancellationTokenSource();

      StartPeerLoading(cancellationPeerLoader.Token);
      
      while (true)
      {
        while (true)
        {
          lock (LOCK_Peers)
          {
            if (Peers.Any())
            {
              PeerSynchronizing = Peers.ElementAt(0);
              Peers.RemoveAt(0);
              break;
            }
          }

          await Task.Delay(3000);
        }
        
        try
        {
          Header headerBranch = await CreateHeaderBranch(PeerSynchronizing);

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
                peer,
                headerFork,
                insertHeaders: false))
              {
                Headerchain.CommitFork();
              }
              else
              {
                UTXOTable.RestoreFromDisk();
                peer.ReportInvalid();
                continue;
              }
            }

            await UTXOSynchronization(
              peer,
              headerBranch,
              insertHeaders: true);
          }

          lock (LOCK_PeersSynchronizationDone)
          {
            PeersSynchronizationDone.Add(PeerSynchronizing);

            if(PeersSynchronizationDone.Count >= Network.COUNT_PEERS_OUTBOUND)
            {
              cancellationPeerLoader.Cancel();
              break;
            }
          }

        }
        catch (Exception ex)
        {
          Console.WriteLine(
            string.Format("Exception {0}: {1} when syncing with peer {2}",
            ex.GetType(),
            ex.Message,
            PeerSynchronizing.GetIdentification()));

          PeerSynchronizing.ReportInvalid();
        }

      }

      await SignalPeerLoadingCompleted.Task;

      Peers.ForEach(p => p.Release());
      PeersSynchronizationDone.ForEach(p => p.Release());

      StartListener();
    }


    async Task RunBlockchainPeer()
    {
      while (true)
      {
        BlockchainPeer peer = new BlockchainPeer(
          await Network.CreateNetworkPeer());
        
        lock (LOCK_Peers)
        {
          Peers.Add(peer);

          Console.WriteLine(
            "Created peer {0}, total {1} peers created.",
            peer.GetIdentification(),
            Peers.Count);
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


    object LOCK_PeersSynchronizationDone = new object();
    List<BlockchainPeer> PeersSynchronizationDone = 
      new List<BlockchainPeer>();
    TaskCompletionSource<object> SignalPeerLoadingCompleted =
      new TaskCompletionSource<object>();

    async Task StartPeerLoading(CancellationToken cancellationToken)
    {
      bool isPeerRequested = false;

      try
      {
        while (true)
        {
          lock (LOCK_Peers)
            lock (LOCK_PeersSynchronizationDone)
            {
              if (Peers.Count + PeersSynchronizationDone.Count
                < Network.COUNT_PEERS_OUTBOUND)
              {
                isPeerRequested = true;
              }
            }

          if(isPeerRequested)
          {
            var peer = new BlockchainPeer(
              await Network.DispatchPeerOutbound(cancellationToken));

            lock (LOCK_Peers)
            {
              Peers.Add(peer);
            }
          }
          else
          {
            await Task.Delay(3000, cancellationToken);
          }
        }
      }
      catch(TaskCanceledException)
      {
        SignalPeerLoadingCompleted.SetResult(null);
      }
    }

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
        new Task[Network.COUNT_PEERS_OUTBOUND];
      
      tasksUTXOSyncSession[0] = RunUTXOSyncSession(peer);

      for (int i = 1; i < Network.COUNT_PEERS_OUTBOUND; i += 1)
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
