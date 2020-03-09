using System;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Threading.Tasks.Dataflow;

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
      // First implement archiving, only then loading

      //  await HeaderArchive.Load(
      //  Headerchain.GenesisHeader.HeaderHash);
      
      //await UTXOArchive.Load(
      //  UTXOTable.Header.HeaderHash);

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


    Headerchain.HeaderBranch HeaderBranch;
    Header HeaderLoad;
    bool FlagAllBatchesLoaded;

    async Task SynchronizeWithPeer(BlockchainPeer peer)
    {
      try
      {
        HeaderBranch = await Headerchain.CreateHeaderBranch(peer);

        if (HeaderBranch != null)
        {
          Headerchain.StageBranch(HeaderBranch);

          if (HeaderBranch.IsFork)
          {
            UTXOTable.BackupToDisk();

            if (!TryRollBackUTXO(
              HeaderBranch.HeaderRoot.HashPrevious))
            {
              // Reset everything, prepare for reindexing
              // If possible, try to use the UTXOImage

              throw new NotImplementedException();
            }
          }
          
          HeaderLoad = HeaderBranch.HeaderRoot;
          
          while (true)
          {
            var peersIdle = new List<BlockchainPeer>();

            lock (LOCK_Peers)
            {
              if (Peers.All(p => p.IsStatusCompleted()))
              {
                break;
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

          if (HeaderBranch.IsFork && 
            !HeaderBranch.IsForkTipInserted)
          {
            UTXOTable.RestoreFromDisk();
            peer.Dispose();
            return;
          }

          if(!HeaderBranch.IsHeaderTipInserted)
          {
            peer.Dispose();
          }

          Headerchain.CommitBranch(HeaderBranch);
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
         
    bool TryRollBackUTXO(byte[] headerHash)
    {
      return true;
    }
    


    readonly object LOCK_BatchIndex = new object();
    int BatchIndex;

    async Task RunUTXOSyncSession(BlockchainPeer peer)
    {      
      if (peer.UTXOBatchesDownloaded.Count == 0)
      {
        if(!TryLoadBatch(
          peer.CountBlocksLoad,
          out DataBatch uTXOBatch))
        {
          FlagAllBatchesLoaded = true;
          peer.SetStatusIdle();
          return;
        }

        while(!await peer.TryDownloadBlocks(uTXOBatch))
        {
          while(true)
          {
            lock (LOCK_Peers)
            {
              if (Peers.All(p => p.IsStatusCompleted()))
              {
                return;
              }

              peer =
                Peers.Find(p => p.IsStatusIdle()) ??
                Peers.Find(p =>
                  p.IsStatusAwaitingInsertion() &&
                  p.UTXOBatchesDownloaded.Peek().Index > uTXOBatch.Index);

              if (peer != null)
              {
                peer.SetStatusBusy();
                break;
              }
            }

            await Task.Delay(1000);
          }
        }
      }

      lock (LOCK_BatchIndex)
      {
        if (peer.UTXOBatchesDownloaded.Peek().Index != 
          BatchIndex)
        {
          peer.SetStatusAwaitingInsertion();
          return;
        }
      }

      if(!IsUTXOBatchInserterRunning)
      {
        RunUTXOBatchInserter();
      }

      while (true)
      {
        QueuePeersUTXOInserter.Post(peer);

        lock (LOCK_BatchIndex)
        {
          BatchIndex += 1;
        }

        lock (LOCK_Peers)
        {
          peer = Peers.Find(p =>
          p.IsStatusAwaitingInsertion() &&
          p.UTXOBatchesDownloaded.Peek().Index == BatchIndex);
        }

        if (peer == null)
        {
          return;
        }

        peer.SetStatusBusy();
      }
    }



    BufferBlock<BlockchainPeer> QueuePeersUTXOInserter = 
      new BufferBlock<BlockchainPeer>();
    bool IsUTXOBatchInserterRunning;

    async Task RunUTXOBatchInserter()
    {
      IsUTXOBatchInserterRunning = true;

      while (true)
      {
        BlockchainPeer peer = await QueuePeersUTXOInserter
          .ReceiveAsync()
          .ConfigureAwait(false);

        DataBatch uTXOBatch = peer.UTXOBatchesDownloaded.Pop();

        foreach (UTXOTable.BlockContainer blockContainer in
          uTXOBatch.DataContainers)
        {
          try
          {
            UTXOTable.InsertBlockContainer(blockContainer);
          }
          catch (ChainException ex)
          {
            Console.WriteLine(
              "Exception when inserting block {1}: \n{2}",
              blockContainer.Header.Hash.ToHexString(),
              ex.Message);

            peer.Dispose();
            return;
          }

          HeaderBranch.ReportHeaderInsertion(
            blockContainer.Header);
        }

        if (uTXOBatch.IsCancellationBatch)
        {
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

    bool TryLoadBatch(
      int countHeaders, 
      out DataBatch uTXOBatch)
    {
      lock (LOCK_LoadBatch)
      {
        if (HeaderLoad.HeaderNext == null)
        {
          uTXOBatch = null;
          return false;
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

        return true;
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
        headerContainer.HeaderRoot.Hash, 
        out Header header))
      {
        if (UTXOTable
          .Synchronizer
          .MapBlockToArchiveIndex
          .ContainsKey(headerContainer.HeaderRoot.Hash))
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
