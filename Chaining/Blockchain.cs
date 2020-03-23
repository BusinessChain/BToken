using System;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Threading.Tasks.Dataflow;
using System.Security.Cryptography;

using BToken.Networking;


namespace BToken.Chaining{
  public partial class Blockchain
  {
    public const int COUNT_PEERS_MAX = 4;

    public Headerchain Headerchain;
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
    readonly object LOCK_IsChainLocked = new object();
    bool IsChainLocked;

    public async Task Start()
    {
      // First implement archiving, only then loading

      //  await HeaderArchive.Load(
      //  Headerchain.GenesisHeader.HeaderHash);

      //await UTXOArchive.Load(
      //  UTXOTable.Header.HeaderHash);

      StartPeerGenerator();
      StartPeerSynchronizer();
    }

    async Task StartPeerGenerator()
    {
      while(true)
      {
        bool flagCreatePeer = false;

        lock (LOCK_Peers)
        {
          Peers.RemoveAll(p => p.IsStatusDisposed());

          if (Peers.Count < COUNT_PEERS_MAX)
          {
            flagCreatePeer = true;
          }
        }

        if (flagCreatePeer)
        {
          var peer = new BlockchainPeer(
            this,
            await Network.CreateNetworkPeer());

          peer.StartListener();

          lock (LOCK_Peers)
          {
            Peers.Add(peer);
          }
        }

        await Task.Delay(2000);
      }
    }

    async Task StartPeerSynchronizer()
    {
      while (true)
      {
        await Task.Delay(1000);

        BlockchainPeer peerSynchronizing = null;

        lock (LOCK_IsChainLocked)
        {
          if (IsChainLocked)
          {
            continue;
          }

          lock (LOCK_IsSynchronizing)
          {
            if (IsSynchronizing)
            {
              continue;
            }

            lock (LOCK_Peers)
            {
              peerSynchronizing = Peers.Find(p =>
                !p.IsSynchronized && p.IsStatusIdle());
            }

            if (peerSynchronizing != null)
            {
              IsChainLocked = true;
            }
          }
        }

        if (peerSynchronizing != null)
        {
          await SynchronizeWithPeer(peerSynchronizing);
        }

        IsChainLocked = false;
      }
    }

    Headerchain.HeaderBranch HeaderBranch;
    Header HeaderLoad;
    bool FlagAllBatchesLoaded;

    public async Task SynchronizeWithPeer(BlockchainPeer peer)
    {
      IsSynchronizing = true;
      peer.SetIsSynchronizing();

      HeaderBranch = null;

      try
      {
        List<byte[]> locator = Headerchain.GetLocator();
        
        HeaderContainer headerContainer = 
          await peer.GetHeaders(locator);
        
        if (headerContainer.HeaderRoot != null)
        {
          HeaderBranch = Headerchain.CreateBranch();

          HeaderContainer headerContainerNext;

          while(true)
          {
            HeaderBranch.AddContainer(headerContainer.HeaderRoot);

            headerContainerNext = await peer.GetHeaders(locator);

            if(headerContainerNext.HeaderRoot == null)
            {
              break;
            }

            if (!headerContainerNext.HeaderRoot.HeaderPrevious.Hash
              .IsEqual(headerContainer.HeaderTip.Hash))
            {
              throw new ChainException(
                "Received header container does not link to chain.");
            }

            headerContainer = headerContainerNext;
          }
        
          if (HeaderBranch.AccumulatedDifficulty <= 
            Headerchain.AccumulatedDifficulty)
          {
            if (peer.IsInbound())
            {
              throw new ChainException(
                string.Format(
                  "Received header branch is weaker than main chain.",
                  peer.GetIdentification()));
            }

            HeaderBranch = null;

            peer.SendHeaders(
              new List<Header>() { Headerchain.HeaderTip });
          }
        }
      }
      catch (Exception ex)
      {
        Console.WriteLine(
          string.Format("Exception {0} when syncing with peer {1}: \n{2}",
          ex.GetType(),
          peer.GetIdentification(),
          ex.Message));

        peer.Dispose();
      }

      if (HeaderBranch != null)
      {
        Header header = Headerchain.HeaderTip;

        if (header != HeaderBranch.HeaderAncestor)
        {
          UTXOTable.BackupToDisk();

          do
          {
            try
            {
              var blockContainer = new UTXOTable.BlockContainer(header);
              blockContainer.Parse();
              UTXOTable.RollBack(blockContainer);
            }
            catch
            {
              // Reset everything, prepare for reindexing
              // If possible, try to use the UTXOImage

              throw new NotImplementedException();
            }

            header = header.HeaderPrevious;
          } while (header != HeaderBranch.HeaderAncestor);
        }

        HeaderLoad = HeaderBranch.HeaderRoot;

        while (true)
        {
          var peersIdle = new List<BlockchainPeer>();

          lock (LOCK_Peers)
          {
            if (Peers.All(p => p.IsStatusCompleted()))
            {
              if (!HeaderBranch.AreAllHeadersInserted)
              {
                peer.Dispose();
              }

              break;
            }

            if (!FlagAllBatchesLoaded)
            {
              peersIdle = Peers.FindAll(p => p.IsStatusIdle());
              peersIdle.ForEach(p => p.SetStatusBusy());
            }
          }

          peersIdle.Select(p => RunUTXOSyncSession(p))
            .ToList();

          await Task.Delay(1000);
        }

        if (HeaderBranch.AccumulatedDifficultyInserted >
          Headerchain.AccumulatedDifficulty)
        {
          Headerchain.CommitBranch(HeaderBranch);
        }
        else
        {
          UTXOTable.RestoreFromDisk();
        }
      }

      IsSynchronizing = false;

      peer.IsSynchronized = true;
      peer.ClearIsSynchronizing();
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

        peer.CalculateNewCountBlocks();
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



    readonly object LOCK_IsSynchronizing = new object();
    bool IsSynchronizing;


    public async Task InsertHeader(
      Header header,
      BlockchainPeer peer)
    {
      while (true)
      {
        lock (LOCK_IsChainLocked)
        {
          if (IsChainLocked)
          {
            if (IsSynchronizing)
            {
              return;
            }
          }
          else
          {
            IsChainLocked = true;
            break;
          }
        }

        await Task.Delay(200);
      }

      if (Headerchain.ContainsHeader(
        header.Hash))
      {
        Header headerContained = Headerchain.HeaderTip;

        int depthDuplicateAcceptedMax = 3;
        int depthDuplicate = 0;

        while (depthDuplicate < depthDuplicateAcceptedMax)
        {
          if (headerContained.Hash.IsEqual(header.Hash))
          {
            if (peer.HeaderDuplicates.Any(h => h.IsEqual(header.Hash)))
            {
              throw new ChainException(
                string.Format(
                  "Received duplicate header {0} more than once.",
                  header.Hash.ToHexString()));
            }

            peer.HeaderDuplicates.Add(header.Hash);
            if (peer.HeaderDuplicates.Count > depthDuplicateAcceptedMax)
            {
              peer.HeaderDuplicates = peer.HeaderDuplicates.Skip(1)
                .ToList();
            }

            break;
          }

          if (headerContained.HeaderPrevious != null)
          {
            break;
          }

          headerContained = header.HeaderPrevious;
          depthDuplicate += 1;
        }

        if(depthDuplicate == depthDuplicateAcceptedMax)
        {
          throw new ChainException(
            string.Format(
              "Received duplicate header {0} with depth greater than {1}.",
              header.Hash.ToHexString(),
              depthDuplicateAcceptedMax));
        }
      }
      else if (Headerchain.HeaderTip.Hash.IsEqual(
        header.HashPrevious))
      {
        var headerBranch = Headerchain.CreateBranch();

        headerBranch.AddContainer(header);
        
        var blockBatch = new DataBatch(IndexLoad++);

        var blockContainer =
          new UTXOTable.BlockContainer(
            Headerchain,
            header);

        blockBatch.DataContainers.Add(blockContainer);

        if (await peer.TryDownloadBlocks(blockBatch))
        {
          UTXOTable.InsertBlockContainer(blockContainer);

          headerBranch.ReportHeaderInsertion(header);

          Headerchain.CommitBranch(headerBranch);
        }
        else
        {
          throw new ChainException(
            string.Format(
              "Could not download announced block {0}", 
              header.Hash.ToHexString()));
        }
      }
      else
      {
        await SynchronizeWithPeer(peer);

        if (!Headerchain.ContainsHeader(
          header.Hash))
        {
          throw new ChainException(
            string.Format(
              "Advertized header {0} could not" + 
              "be inserted in Headerchain",
              header.Hash.ToHexString()));
        }
      }

      IsChainLocked = false;
    }
  }
}
