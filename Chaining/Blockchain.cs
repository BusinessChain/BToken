using System;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Threading.Tasks.Dataflow;
using System.Security.Cryptography;

using BToken.Networking;


namespace BToken.Chaining
{
  public partial class Blockchain
  {
    public const int COUNT_PEERS_MAX = 4;

    Header HeaderGenesis;

    Header HeaderTip;
    double Difficulty;
    int Height;

    Header HeaderRootStaged;
    Header HeaderTipStaged;
    double DifficultyStaged;
    int HeightStaged;
    int HeightRootStaged;

    HeaderLocator Locator;

    List<HeaderLocation> Checkpoints;
    
    readonly object HeaderIndexLOCK = new object();
    Dictionary<int, List<Header>> HeaderIndex;

    BranchInserter Branch;

    UTXOTable UTXOTable;

    Network Network;
    
    int SIZE_HEADER_ARCHIVE = 2000;
    
    int SIZE_BLOCK_ARCHIVE = 50000;
    int IndexBlockArchive;
    FileStream FileArchiveBlock;
    
    SHA256 SHA256 = SHA256.Create();

    int IndexBlock;
    int IndexImageHeader;
    const string PathIndexBlock = "IndexUTXO";



    public Blockchain(
      Header headerGenesis,
      byte[] genesisBlockBytes,
      List<HeaderLocation> checkpoints,
      Network network)
    {
      HeaderTip = headerGenesis;
      HeaderGenesis = headerGenesis;

      Checkpoints = checkpoints;

      Network = network;

      Branch = new BranchInserter(this);

      Locator = new HeaderLocator();

      HeaderIndex = new Dictionary<int, List<Header>>();
      UpdateHeaderIndex(headerGenesis);
      
      UTXOTable = new UTXOTable(genesisBlockBytes);
    }

    void Initialize()
    {
      HeaderTip = HeaderGenesis;
      Height = 0;
      Difficulty = HeaderGenesis.Difficulty;

      Branch.Initialize();

      Locator.Locations.Clear();

      HeaderIndex.Clear();
      UpdateHeaderIndex(HeaderGenesis);

      UTXOTable.Clear();
      IndexBlock = 0;

      Archive.Initialize();
    }
    


    object LOCK_Peers = new object();
    List<BlockchainPeer> Peers = new List<BlockchainPeer>();
    readonly object LOCK_IsBlockchainLocked = new object();
    bool IsBlockchainLocked;
    readonly object LOCK_IndexBlockArchiveLoad = new object();
    int IndexBlockArchiveLoad;

    public async Task Start()
    {
      LoadImage();

      await LoadBlocks();

      StartPeerGenerator();
      StartPeerSynchronizer();
    }

    void LoadImage()
    {
      try
      {
        var imageHeader = new HeaderContainer();

        while (true)
        {
          string path = Path.Combine(
              "Headerchain",
              IndexImageHeader.ToString());

          if (!File.Exists(path))
          {
            break;
          }

          imageHeader.Buffer = File.ReadAllBytes(path);

          imageHeader.Parse(SHA256);

          StageHeaderchain(imageHeader.HeaderRoot);
          CommitHeaderchain(imageHeader);

          IndexImageHeader += 1;
        }

        IndexBlock = BitConverter.ToInt32(
          File.ReadAllBytes(PathIndexBlock),
          0);

        UTXOTable.LoadImage();
      }
      catch
      {
        Initialize();
      }
    }

    async Task StartPeerGenerator()
    {
      bool flagCreatePeer;

      while (true)
      {
        flagCreatePeer = false;

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

          continue;
        }

        await Task.Delay(2000);
      }
    }

    async Task StageHeaderchain(BlockchainPeer peer)
    {
      List<byte[]> locator = Locator.Locations
        .Select(b => b.Hash)
        .ToList();

      try
      {
        Header header = await peer.GetHeaders(locator);

        while (header != null)
        {
          StageHeaderchain(header);

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
    void StageHeaderchain(Header header)
    {
      if(HeaderTipStaged == null)
      {
        HeaderTipStaged = HeaderTip;
        DifficultyStaged = Difficulty;
        HeightStaged = Height;

        if(!header.HashPrevious.IsEqual(
          HeaderTipStaged.Hash))
        {
          do
          {
            DifficultyStaged -= HeaderTipStaged.Difficulty;

            HeightStaged--;

            HeaderTipStaged = HeaderTipStaged.HeaderPrevious;
          } while (!header.HashPrevious.IsEqual(
            HeaderTipStaged.Hash));

          while (header.Hash.IsEqual(
              HeaderTipStaged.HeaderNext.Hash))
          {
            HeaderTipStaged = HeaderTipStaged.HeaderNext;

            DifficultyStaged += HeaderTipStaged.Difficulty;

            HeightStaged += 1;

            if (header.HeaderNext == null)
            {
              return;
            }

            header = header.HeaderNext;
          }
        }

        HeaderRootStaged = header;
        HeightRootStaged = HeightStaged + 1;
      }
      else if (!header.HashPrevious.IsEqual(
        HeaderTipStaged.Hash))
      {
        throw new ChainException(
          "Received header does not link to last header.");
      }

      HeaderTipStaged.HeaderNext = header;
      header.HeaderPrevious = HeaderTipStaged;

      while (true)
      {
        ValidateHeader(header);

        DifficultyStaged += header.Difficulty;
        HeightStaged += 1;

        if (header.HeaderNext == null)
        {
          HeaderTipStaged = header;
          return;
        }

        header = header.HeaderNext;
      }
    }

    void CommitHeaderchain()
    {
      HeaderTip = HeaderTipStaged;
      Difficulty += DifficultyStaged;
      Height = HeightStaged;

      HeaderTipStaged = null;
    }



    void InsertHeaderchain(UTXOTable.BlockArchive blockArchive)
    {
      if(IsFork)
      {

      }
      else
      {
        HeaderTip = blockArchive.HeaderTip;
        Difficulty += blockArchive.Difficulty;
        Height = blockArchive.Height;

        HeaderTipStaged = null;
      }

      Branch.ReportBlockInsertion(blockContainer.HeaderTip);

      //if (Branch.IsFork)
      //{
      //  Branch.Archive.ArchiveContainer(blockContainer);

      //  if (Branch.DifficultyInserted > Difficulty)
      //  {
      //    Branch.Archive.Export(Archive);

      //    Branch.IsFork = false;
      //  }
      //}
      //else
      //{
      //  ArchiveContainer(blockContainer);
      //}
    }


    const int COUNT_INDEXER_TASKS = 8;
    Task[] LoaderTasks = new Task[COUNT_INDEXER_TASKS];

    async Task LoadBlocks()
    {
      IndexBlockArchiveLoad = IndexBlock + 1;

      Parallel.For(
        0,
        COUNT_INDEXER_TASKS,
        i => LoaderTasks[i] = StartLoader());

      await Task.WhenAll(LoaderTasks);
    }

    async Task StartLoader()
    {
      UTXOTable.BlockArchive blockArchive = null;
      SHA256 sHA256 = SHA256.Create();
      
    LABEL_LoadBlockArchive:

      while (true)
      {
        LoadBlockArchive(
          sHA256, 
          ref blockArchive);
        
        while (true)
        {
          if (IsBlockLoadingCompleted)
          {
            return;
          }

          lock (LOCK_QueueBlockArchives)
          {
            if (blockArchive.Index == IndexBlock + 1)
            {
              break;
            }

            if (QueueBlockArchives.Count < 10)
            {
              QueueBlockArchives.Add(
                blockArchive.Index,
                blockArchive);

              if(blockArchive.IsValid)
              {
                blockArchive = null;
                goto LABEL_LoadBlockArchive;
              }

              return;
            }
          }

          await Task.Delay(2000).ConfigureAwait(false);
        }
        
        try
        {
          while (blockArchive.IsValid)
          {
            StageHeaderchain(blockArchive.HeaderRoot);

            UTXOTable.InsertBlockArchive(blockArchive);
            IndexBlock += 1;

            CommitHeaderchain(blockArchive);

            lock (LOCK_QueueBlockArchives)
            {
              if (QueueBlockArchives.TryGetValue(
                blockArchive.Index + 1, 
                out UTXOTable.BlockArchive blockArchiveNext))
              {
                QueueBlockArchives.Remove(blockArchiveNext.Index);

                lock (LOCK_BlockArchivesIdle)
                {
                  BlockArchivesIdle.Add(blockArchive);
                }

                blockArchive = blockArchiveNext;
              }
              else
              {
                break;
              }
            }
          }
        }
        catch (ChainException)
        {
          IsBlockLoadingCompleted = true;
          return;
        }
      }
    }



    readonly object LOCK_BlockArchivesIdle = new object();
    List<UTXOTable.BlockArchive> BlockArchivesIdle =
      new List<UTXOTable.BlockArchive>();

    void LoadBlockArchive(
      SHA256 sHA256,
      ref UTXOTable.BlockArchive blockArchive)
    {
      if(blockArchive == null)
      {
        lock (LOCK_BlockArchivesIdle)
        {
          if (BlockArchivesIdle.Count == 0)
          {
            blockArchive = new UTXOTable.BlockArchive();
          }
          else
          {
            blockArchive = BlockArchivesIdle.Last();
            BlockArchivesIdle.Remove(blockArchive);
          }
        }
      }

      lock (LOCK_IndexBlockArchiveLoad)
      {
        blockArchive.Index = IndexBlockArchiveLoad;
        IndexBlockArchiveLoad += 1;
      }

      try
      {
        blockArchive.Buffer = File.ReadAllBytes(
        Path.Combine(
          ArchiveDirectory.Name,
          blockArchive.Index.ToString()));

        blockArchive.Parse(sHA256);

        blockArchive.IsValid = true;
      }
      catch
      {
        blockArchive.IsValid = false;
      }
    }



    bool IsBlockLoadingCompleted;
    readonly object LOCK_QueueBlockArchives = new object();
    Dictionary<int, UTXOTable.BlockArchive> QueueBlockArchives =
      new Dictionary<int, UTXOTable.BlockArchive>();
    
    void ValidateHeader(Header header)
    {
      uint medianTimePast = GetMedianTimePast(
      header.HeaderPrevious);

      if (header.UnixTimeSeconds < medianTimePast)
      {
        throw new ChainException(
          string.Format(
            "Header {0} with unix time {1} " +
            "is older than median time past {2}.",
            header.Hash.ToHexString(),
            DateTimeOffset.FromUnixTimeSeconds(header.UnixTimeSeconds),
            DateTimeOffset.FromUnixTimeSeconds(medianTimePast)),
          ErrorCode.INVALID);
      }

      int hightHighestCheckpoint = Checkpoints.Max(x => x.Height);

      if (
        hightHighestCheckpoint <= Height &&
        Height <= hightHighestCheckpoint)
      {
        throw new ChainException(
          string.Format(
            "Attempt to insert header {0} at hight {1} " +
            "prior to checkpoint hight {2}",
            header.Hash.ToHexString(),
            Height,
            hightHighestCheckpoint),
          ErrorCode.INVALID);
      }

      HeaderLocation checkpoint =
        Checkpoints.Find(c => c.Height == Height);
      if (
        checkpoint != null &&
        !checkpoint.Hash.IsEqual(header.Hash))
      {
        throw new ChainException(
          string.Format(
            "Header {0} at hight {1} not equal to checkpoint hash {2}",
            header.Hash.ToHexString(),
            Height,
            checkpoint.Hash.ToHexString()),
          ErrorCode.INVALID);
      }

      uint targetBits = TargetManager.GetNextTargetBits(
          header.HeaderPrevious,
          (uint)Height);

      if (header.NBits != targetBits)
      {
        throw new ChainException(
          string.Format(
            "In header {0} nBits {1} not equal to target nBits {2}",
            header.Hash.ToHexString(),
            header.NBits,
            targetBits),
          ErrorCode.INVALID);
      }
    }

    static uint GetMedianTimePast(Header header)
    {
      const int MEDIAN_TIME_PAST = 11;

      List<uint> timestampsPast = new List<uint>();

      int depth = 0;
      while (depth < MEDIAN_TIME_PAST)
      {
        timestampsPast.Add(header.UnixTimeSeconds);

        if (header.HeaderPrevious == null)
        { break; }

        header = header.HeaderPrevious;
        depth++;
      }

      timestampsPast.Sort();

      return timestampsPast[timestampsPast.Count / 2];
    }


    async Task StartPeerSynchronizer()
    {
      BlockchainPeer peer;

      while (true)
      {
        await Task.Delay(2000);

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

        lock (LOCK_IsBlockchainLocked)
        {
          if (IsBlockchainLocked)
          {
            continue;
          }

          IsBlockchainLocked = true;
        }

        await SynchronizeWithPeer(peer);

        peer.IsSynchronized = true;

        IsBlockchainLocked = false;
      }
    }



    bool IsFork;
    double DifficultyStagedInserted;
    int HeightInserted;
    Header HedaerTipInserted;

    async Task SynchronizeWithPeer(BlockchainPeer peer)
    {
      await StageHeaderchain(peer);

      if (DifficultyStaged > Difficulty)
      {
        if (HeightRootStaged < Height)
        {
          IsFork = true;

          if(HeightImage < HeightRootStaged)
          {
            LoadImage();
          }

          // Reindex until HeightRootStaged - 1
        }

        StartUTXOSyncSessions();

        await RunUTXOInserter();

        if (DifficultyStagedInserted <= Difficulty)
        {
          HeaderTipStaged = null;

          UTXOTable.Restore();
          Archive.Restore();
        }

        if (DifficultyStagedInserted < DifficultyStaged)
        {
          peer.Dispose();
        }
      }
      else if (DifficultyStaged < Difficulty)
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
      HeaderLoad = HeaderRootStaged;

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

    void CommitBranchInserted()
    {
      Branch.HeaderAncestor.HeaderNext =
        Branch.HeaderRoot;

      HeaderTip = Branch.HeaderTipInserted;
      Difficulty = Branch.DifficultyInserted;
      Height = Branch.HeightInserted;

      Archive.CommitBranch();
    }
    
    void InsertBranch()
    {
      Branch.HeaderAncestor.HeaderNext =
        Branch.HeaderRoot;

      HeaderTip = Branch.HeaderTip;
      Difficulty = Branch.Difficulty;
      Height = Branch.Height;
    }



    readonly object LOCK_BatchIndex = new object();
    int BatchIndex;

    async Task RunUTXOSyncSession(BlockchainPeer peer)
    {
      if (peer.UTXOBatches.Count == 0)
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
          uTXOBatch, 
          headers))
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
                  p.UTXOBatches.Peek().Index > uTXOBatch.Index);

              if (peer != null)
              {
                peer.SetStatusBusy();
                break;
              }
            }

            await Task.Delay(1000);
          }
        }

        peer.UTXOBatches.Push(uTXOBatch);

        peer.CalculateNewCountBlocks();
      }

      lock (LOCK_BatchIndex)
      {
        if (peer.UTXOBatches.Peek().Index !=
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
          p.UTXOBatches.Peek().Index == BatchIndex);
        }

        if (peer == null)
        {
          return;
        }

        peer.SetStatusBusy();
      }
    }



    Header HeaderLoad;
    bool FlagAllHeadersLoaded;
    BufferBlock<BlockchainPeer> QueuePeersUTXOInserter =
      new BufferBlock<BlockchainPeer>();

    async Task RunUTXOInserter()
    {
      while (true)
      {
        BlockchainPeer peer = await QueuePeersUTXOInserter
          .ReceiveAsync()
          .ConfigureAwait(false);

        DataBatch uTXOBatch = peer.UTXOBatches.Pop();

        foreach (UTXOTable.BlockArchive archiveBlock in
          uTXOBatch.DataContainers)
        {
          archiveBlock.Index = IndexBlock;

          try
          {
            UTXOTable.InsertBlockArchive(archiveBlock);
          }
          catch (ChainException ex)
          {
            Console.WriteLine(
              "Exception when inserting block {1}: \n{2}",
              archiveBlock.HeaderTip.Hash.ToHexString(),
              ex.Message);
            
            peer.Dispose();
            return;
          }

          DifficultyStagedInserted += archiveBlock.Difficulty;

          if (IsFork)
          {
            // archive to fork

            if (DifficultyStagedInserted > Difficulty)
            {
              IsFork = false;
              // reorg archive
            }
          }
          else
          {
            InsertHeaderchain(archiveBlock.HeaderRoot);

            // Why does locator require height?
            Locator.Generate(Height, HeaderTip);

            // Archive to main
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

    void ArchiveContainer(
      UTXOTable.BlockArchive archiveBlock)
    {
      HeaderArchiveQueue.Add(archiveBlock.HeaderRoot);

      if (HeaderArchiveQueue.Count >= SIZE_HEADER_ARCHIVE)
      {
        HeaderArchiveQueue.ForEach(
          h => WriteToArchive(HeaderArchive, h.GetBytes()));

        HeaderArchiveQueue.Clear();

        CreateHeaderArchive();
      }

      BlockContainers.Add(archiveBlock);
      CountTXs += archiveBlock.CountTX;

      if (CountTXs >= SIZE_BLOCK_ARCHIVE)
      {
        BlockContainers.ForEach(
          c => WriteToArchive(BlockArchive, c.Buffer));

        BlockContainers.Clear();

        if (IndexBlockArchive % UTXOIMAGE_INTERVAL == 0)
        {
          UTXOTable.ArchiveImage(IndexBlockArchive);
        }

        IndexBlockArchive += 1;

        CreateBlockArchive();
      }
    }



    int IndexLoad;
    readonly object LOCK_HeaderLoad = new object();

    List<Header> LoadHeaders(int countHeaders)
    {
      List<Header> headers = new List<Header>();

      lock (LOCK_HeaderLoad)
      {
        for (int i = 0; i < countHeaders; i += 1)
        {
          headers.Add(HeaderLoad);

          HeaderLoad = HeaderLoad.HeaderNext;

          if (HeaderLoad == null)
          {
            break;
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
        lock (LOCK_IsBlockchainLocked)
        {
          if (IsBlockchainLocked)
          {
            countLockTriesRemaining -= 1;
          }
          else
          {
            IsBlockchainLocked = true;
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
        
        UTXOTable.BlockArchive blockContainer =
          await peer.DownloadBlock(header);
                
        blockContainer.Index = IndexBlock;

        UTXOTable.InsertBlockArchive(blockContainer);

        Archive.ArchiveContainer(blockContainer);
        // hier soll nicht mit derselben Kadenz ein Image 
        // gemacht werden wie beim Indexieren

        Branch.ReportBlockInsertion(header);

        Locator.AddLocation(Height, header.Hash);

        CommitBranchInserted();
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

      IsBlockchainLocked = false;
    }



    List<UTXOTable.BlockArchive> BlockContainers = 
      new List<UTXOTable.BlockArchive>();
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
