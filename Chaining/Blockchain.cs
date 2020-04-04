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

    Header HeaderRoot;
    Header HeaderTip;
    double Difficulty;
    int Height;
    
    static List<HeaderLocation> Checkpoints;

    BranchInserter Branch;

    readonly object HeaderIndexLOCK = new object();
    Dictionary<int, List<Header>> HeaderIndex;
    
    HeaderLocator Locator;

    DataArchiver HeaderArchive;
    
    UTXOTable UTXOTable;
    DataArchiver Archive;
    
    Network Network;



    public Blockchain(
      Header genesisHeader,
      byte[] genesisBlockBytes,
      List<HeaderLocation> checkpoints,
      Network network)
    {
      HeaderRoot = genesisHeader;
      HeaderTip = genesisHeader;
      Height = 0;
      Difficulty = TargetManager.GetDifficulty(
        genesisHeader.NBits);

      Checkpoints = checkpoints;

      Branch = new BranchInserter(this);

      Locator = new HeaderLocator();

      HeaderIndex = new Dictionary<int, List<Header>>();
      UpdateHeaderIndex(genesisHeader);
      
      UTXOTable = new UTXOTable(genesisBlockBytes);

      Archive = new DataArchiver(
        UTXOTable,
        4);

      Network = network;
    }

    
    
    object LOCK_Peers = new object();
    List<BlockchainPeer> Peers = new List<BlockchainPeer>();
    readonly object LOCK_IsChainLocked = new object();
    bool IsChainLocked;

    public async Task Start()
    {
      await Archive.Load();

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

        await SynchronizeWithPeer(peer);

        peer.IsSynchronized = true;

        IsChainLocked = false;
      }
    }

    async Task SynchronizeWithPeer(BlockchainPeer peer)
    {
      Branch.Initialize();

      await Branch.LoadHeaders(peer);

      if (Branch.Difficulty > Difficulty)
      {
        if (Branch.HeaderAncestor != HeaderTip)
        {
          Branch.IsFork = true;
          
          Header header = HeaderTip;

          do
          {
            try
            {
              UTXOTable.BlockContainer blockContainer =
                Branch.Archive.PopContainer(header);
              
              UTXOTable.RollBack(blockContainer);
            }
            catch
            {
              //Indexiere von vorne bis fehlender Block, dann neuen
              //HeaderBranch machen (zuerst Locator updaten)
            }

            header = header.HeaderPrevious;

          } while (header != Branch.HeaderAncestor);
        }

        StartUTXOSyncSessions();

        await RunUTXOInserter();

        if (Branch.DifficultyInserted > Difficulty)
        {
          CommitBranch();

          Locator.Generate(Height, HeaderTip);

          UTXOTable.Backup();
        }
        else
        {
          UTXOTable.Restore();
        }

        if (Branch.DifficultyInserted < Branch.Difficulty)
        {
          peer.Dispose();
        }
      }
      else if (Branch.DifficultyInserted < Difficulty)
      {
        if (peer.IsInbound())
        {
          peer.Dispose();
        }
        else
        {
          peer.SendHeaders(
            new List<Header>() { HeaderTip });
        }
      }
    }
       
    async Task StartUTXOSyncSessions()
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

          if (!FlagAllHeadersLoaded)
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

    void CommitBranch()
    {
      Branch.HeaderAncestor.HeaderNext =
        Branch.HeaderRoot;

      HeaderTip = Branch.HeaderTipInserted;
      Difficulty = Branch.DifficultyInserted;
      Height = Branch.HeightInserted;
    }
    


    Header HeaderLoad;
    bool FlagAllHeadersLoaded;
    BufferBlock<BlockchainPeer> QueuePeersUTXOInserter =
      new BufferBlock<BlockchainPeer>();
    int ArchiveIndex;

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

          Branch.ReportBlockInsertion(blockContainer.Header);

          if (Branch.IsFork)
          {
            Branch.Archive.ArchiveContainer(blockContainer);

            if (Branch.DifficultyInserted > Difficulty)
            {
              Branch.Archive.Export(Archive);

              Branch.IsFork = false;
            }
          }
          else
          {
            Archive.ArchiveContainer(blockContainer);
          }
        }

        if (uTXOBatch.IsCancellationBatch)
        {
          peer.SetStatusCompleted();
          return;
        }

        RunUTXOSyncSession(peer);
      }
    }



    readonly object LOCK_BatchIndex = new object();
    int BatchIndex;

    async Task RunUTXOSyncSession(BlockchainPeer peer)
    {      
      if (peer.UTXOBatchesDownloaded.Count == 0)
      {
        List<Header> headers = LoadHeaders(peer.CountBlocksLoad);

        if (headers.Count == 0)
        {
          FlagAllHeadersLoaded = true;
          peer.SetStatusIdle();
          return;
        }

        DataBatch uTXOBatch = new DataBatch(IndexLoad++);

        while (!await peer.TryDownloadBlocks(
          uTXOBatch, headers))
        {
          while (true)
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
    readonly object LOCK_HeaderLoad = new object();

    List<Header> LoadHeaders(int countHeaders)
    {
      List<Header> headers = new List<Header>();

      lock (LOCK_HeaderLoad)
      {
        if (HeaderLoad.HeaderNext != null)
        {
          for (int i = 0; i < countHeaders; i += 1)
          {
            if (HeaderLoad.HeaderNext == null)
            {
              break;
            }

            HeaderLoad = HeaderLoad.HeaderNext;

            headers.Add(HeaderLoad);
          }
        }
      }

      return headers;
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

      if (ContainsHeader(header.Hash))
      {
        Header headerContained = HeaderTip;

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
      else if (header.HashPrevious.IsEqual(HeaderTip.Hash))
      {
        Branch.Initialize();

        Branch.AddHeaders(header);
        
        UTXOTable.BlockContainer blockContainer =
          await peer.DownloadBlock(header);
                
        blockContainer.Index = ArchiveIndex;

        UTXOTable.InsertBlockContainer(blockContainer);

        Archive.ArchiveContainer(blockContainer);

        Branch.ReportBlockInsertion(header);

        Locator.AddLocation(Height, header.Hash);

        CommitBranch();
      }
      else
      {
        await SynchronizeWithPeer(peer);

        if (!ContainsHeader(header.Hash))
        {
          throw new ChainException(
            string.Format(
              "Advertized header {0} could not be inserted.",
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


    bool ContainsHeader(byte[] headerHash)
    {
      return TryReadHeader(
        headerHash,
        out Header header);
    }

    bool TryReadHeader(
      byte[] headerHash,
      out Header header)
    {
      SHA256 sHA256 = SHA256.Create();

      return TryReadHeader(
        headerHash,
        sHA256,
        out header);
    }

    bool TryReadHeader(
      byte[] headerHash,
      SHA256 sHA256,
      out Header header)
    {
      int key = BitConverter.ToInt32(headerHash, 0);

      lock (HeaderIndexLOCK)
      {
        if (HeaderIndex.TryGetValue(key, out List<Header> headers))
        {
          foreach (Header h in headers)
          {
            if (headerHash.IsEqual(h.Hash))
            {
              header = h;
              return true;
            }
          }
        }
      }

      header = null;
      return false;
    }

    void UpdateHeaderIndex(Header header)
    {
      int keyHeader = BitConverter.ToInt32(header.Hash, 0);

      lock (HeaderIndexLOCK)
      {
        if (!HeaderIndex.TryGetValue(keyHeader, out List<Header> headers))
        {
          headers = new List<Header>();
          HeaderIndex.Add(keyHeader, headers);
        }

        headers.Add(header);
      }
    }

    public List<Header> GetHeaders(
      IEnumerable<byte[]> locatorHashes,
      int count,
      byte[] stopHash)
    {
      foreach (byte[] hash in locatorHashes)
      {
        if (TryReadHeader(hash, out Header header))
        {
          List<Header> headers = new List<Header>();

          while (
            header.HeaderNext != null &&
            headers.Count < count &&
            !header.Hash.IsEqual(stopHash))
          {
            Header nextHeader = header.HeaderNext;

            headers.Add(nextHeader);
            header = nextHeader;
          }

          return headers;
        }
      }

      throw new ChainException(string.Format(
        "Locator does not root in headerchain."));
    }
  }
}
