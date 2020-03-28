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

    // Mainchain könnte als HeaderBranch objekt dargestellt sein.
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
      BlockchainPeer peer;

      while (true)
      {
        await Task.Delay(2000);

        lock (LOCK_IsChainLocked)
        {
          if (IsChainLocked)
          {
            continue;
          }
          
          peer = null;

          lock (LOCK_Peers)
          {
            peer = Peers.Find(p =>
              !p.IsSynchronized && p.IsStatusIdle());
          }

          if (peer == null)
          {
            continue;
          }
          else
          {
            IsChainLocked = true;
          }
        }

        await CreateHeaderBranch(peer);

        if (Branch.Difficulty > Headerchain.Difficulty)
        {
          Header header = Headerchain.HeaderTip;
          while (header != Branch.HeaderAncestor)
          {
            UTXOTable.BlockContainer blockcontainer =
              await UTXOArchive.PopContainer();

            try
            {
              var blockContainer = new UTXOTable.BlockContainer(header);
              blockContainer.Parse();
              UTXOTable.RollBack(blockContainer);
            }
            catch
            {
              //Indexiere von vorne bis fehlender Block, dann neuen
              //HeaderBranch machen (zuerst Locator updaten)
            }

            header = header.HeaderPrevious;
          }
          
          RunUTXOSyncSessions();

          await RunUTXOInserter();

          if (Branch.DifficultyInserted > Headerchain.Difficulty)
          {
            Headerchain.CommitBranch(Branch);
            CommitBranchToArchive();

            UTXOTable.Backup();
          }
          else
          {
            UTXOTable.Restore();
          }

          if (Branch.DifficultyInserted <  Branch.Difficulty)
          {
            peer.Dispose();
          }
        }
        else if (Branch.Difficulty < Headerchain.Difficulty)
        {
          if (peer.IsInbound())
          {
            peer.Dispose();
          }
          else
          {
            peer.SendHeaders(
              new List<Header>() { Headerchain.HeaderTip });
          }
        }

        peer.IsSynchronized = true;

        IsChainLocked = false;
      }
    }

    async Task CreateHeaderBranch(BlockchainPeer peer)
    {
      List<byte[]> locator = Headerchain.GetLocator();
      Branch = Headerchain.CreateBranch();

      try
      {
        Header header = await peer.GetHeaders(locator);

        while (header != null)
        {
          Branch.AddHeaders(header);
          header = await peer.GetHeaders(locator);
        }
      }
      catch (Exception ex)
      {
        peer.Dispose(string.Format(
          "Exception {0} when syncing: \n{1}",
          ex.GetType(),
          ex.Message));
      }
    }

    bool TryRollBackUTXO()
    {
      Header header = Headerchain.HeaderTip;

      while (header != Branch.HeaderAncestor)
      {
        UTXOArchive.RemoveContainer();

        try
        {
          var blockContainer = new UTXOTable.BlockContainer(header);
          blockContainer.Parse();
          UTXOTable.RollBack(blockContainer);
        }
        catch
        {
          return false;
        }

        header = header.HeaderPrevious;
      }

      return true;
    }

    static void CommitBranchToArchive()
    {
      DirectoryInfo main = new DirectoryInfo("main");

      string[] fileNames = Directory.GetFiles("Inserter");

      int i = 0;
      while (i < fileNames.Length)
      {
        string destName = Path.Combine(
          "main",
          Path.GetFileName(fileNames[i]));

        try
        {
          File.Move(fileNames[i], destName);
        }
        catch (IOException)
        {
          File.Delete(destName);
          continue;
        }

        i += 1;
      }
    }



    Headerchain.HeaderBranch Branch;
    Header HeaderLoad;
    bool FlagAllBatchesLoaded;
    BufferBlock<BlockchainPeer> QueuePeersUTXOInserter =
      new BufferBlock<BlockchainPeer>();
    int ArchiveIndex;
    double DifficultyInserted;
    string PathArchive = "PathInserter";

    async Task RunUTXOInserter()
    {
      while (true)
      {
        BlockchainPeer peer = await QueuePeersUTXOInserter
          .ReceiveAsync()
          .ConfigureAwait(false);

        DataBatch uTXOBatch = peer.UTXOBatchesDownloaded.Pop();

        foreach (UTXOTable.BlockContainer blockContainer in
          uTXOBatch.DataContainers)
        {
          blockContainer.Index = ArchiveIndex;

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

          if (DifficultyInserted <= Headerchain.Difficulty)
          {
            DifficultyInserted += blockContainer.AccumulatedDifficulty;

            if (DifficultyInserted > Headerchain.Difficulty)
            {
              ReorganizeArchive();
              PathArchive = "PathMainchain";
            }
          }

          ArchiveContainer(blockContainer, PathArchive);

          Branch.ReportHeaderInsertion(
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

    async Task RunUTXOSyncSessions()
    {
      HeaderLoad = Branch.HeaderRoot;

      while (true)
      {
        var peersIdle = new List<BlockchainPeer>();

        lock (LOCK_Peers)
        {
          if (Peers.All(p => p.IsStatusCompleted()))
          {
            return;
          }

          if (!FlagAllBatchesLoaded)
          {
            peersIdle = Peers.FindAll(p => p.IsStatusIdle());
            peersIdle.ForEach(p => p.SetStatusBusy());
          }
        }

        peersIdle.Select(p => RunUTXOSyncSession(p))
          .ToList();

        await Task.Delay(1000)
          .ConfigureAwait(false);
      }
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

        while(!await peer.TryDownloadBlocks(uTXOBatch)
          .ConfigureAwait(false))
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
    
         


    public async Task InsertHeader(
      Header header,
      BlockchainPeer peer)
    {
      int countLockTriesRemaining = 10;

      while (true)
      {
        lock (LOCK_IsChainLocked)
        {
          if (IsChainLocked)
          {
            countLockTriesRemaining -= 1;
          }
          else
          {
            IsChainLocked = true;
            break;
          }
        }

        if (countLockTriesRemaining == 0)
        {
          return;
        }

        await Task.Delay(500);
      }

      if (Headerchain.ContainsHeader(header.Hash))
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

        if (depthDuplicate == depthDuplicateAcceptedMax)
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

        headerBranch.AddHeaders(header);

        var blockBatch = new DataBatch(IndexLoad++);

        var blockContainer =
          new UTXOTable.BlockContainer(
            Headerchain,
            header);

        blockBatch.DataContainers.Add(blockContainer);

        if (!await peer.TryDownloadBlocks(blockBatch))
        {
          throw new ChainException(
            string.Format(
              "Could not download advertized block {0}",
              header.Hash.ToHexString()));
        }

        // maybe ArchiveIndexStore can be passed directly to InsertBlockContainer
        blockContainer.Index = ArchiveIndex;

        UTXOTable.InsertBlockContainer(blockContainer);

        ArchiveContainer(blockContainer, "pathMain");

        headerBranch.ReportHeaderInsertion(header);

        Headerchain.CommitBranch(headerBranch);
      }
      else
      {
        await SynchronizeWithPeer(peer);

        if (!Headerchain.ContainsHeader(header.Hash))
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



    const int UTXOSTATE_ARCHIVING_INTERVAL = 10;
    const int SIZE_BATCH_ARCHIVE = 50000;
    List<UTXOTable.BlockContainer> BlockContainers = 
      new List<UTXOTable.BlockContainer>();
    int CountTXs;
    DirectoryInfo ArchiveDirectory;

    async Task ArchiveContainer(
      UTXOTable.BlockContainer blockontainer)
    {
      BlockContainers.Add(blockontainer);
      CountTXs += blockontainer.CountItems;

      if (CountTXs >= SIZE_BATCH_ARCHIVE)
      {
        string filePath = Path.Combine(
          ArchiveDirectory.FullName,
          ArchiveIndex.ToString());

        while (true)
        {
          try
          {
            using (FileStream file = new FileStream(
              filePath,
              FileMode.Create,
              FileAccess.Write,
              FileShare.None,
              bufferSize: 65536,
              useAsync: true))
            {
              foreach (DataContainer container in BlockContainers)
              {
                await file.WriteAsync(
                  container.Buffer,
                  0,
                  container.Buffer.Length)
                  .ConfigureAwait(false);
              }
            }

            break;
          }
          catch (IOException ex)
          {
            Console.WriteLine(ex.GetType().Name + ": " + ex.Message);
            await Task.Delay(2000);
            continue;
          }
          catch (Exception ex)
          {
            Console.WriteLine(ex.GetType().Name + ": " + ex.Message);
            break;
          }
        }

        BlockContainers.Clear();
        CountTXs = 0;

        if (ArchiveIndex % UTXOSTATE_ARCHIVING_INTERVAL == 0)
        {
          ArchiveImage();
        }

        ArchiveIndex += 1;
      }
    }
  }
}
