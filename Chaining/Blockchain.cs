using System;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Threading.Tasks.Dataflow;
using System.Security.Cryptography;
using System.Net;
using System.Net.Sockets;



namespace BToken.Chaining
{
  partial class Blockchain
  {
    Header HeaderGenesis;
    List<HeaderLocation> Checkpoints;
    Header HeaderTip;
    double Difficulty;
    int Height;

    readonly object HeaderIndexLOCK = new object();
    Dictionary<int, List<Header>> HeaderIndex;
    
    UTXOTable UTXOTable;
    const int UTXOIMAGE_INTERVAL_SYNC = 500;
    const int UTXOIMAGE_INTERVAL_LISTEN = 50;
    
    BranchInserter Branch;

    readonly object LOCK_IsBlockchainLocked = new object();
    bool IsBlockchainLocked;
    readonly object LOCK_IndexBlockArchiveLoad = new object();
    int IndexBlockArchiveLoad;

    DirectoryInfo ArchiveDirectoryBlocks =
        Directory.CreateDirectory("J:\\BlockArchivePartitioned");
    DirectoryInfo ArchiveDirectoryBlocksFork =
        Directory.CreateDirectory("J:\\BlockArchivePartitioned\\fork");
    DirectoryInfo ArchiveDirectoryHeaders =
        Directory.CreateDirectory("headerchain");
    DirectoryInfo ArchiveDirectoryHeadersFork =
        Directory.CreateDirectory("headerchain\\fork");

    DirectoryInfo DirectoryImage =
        Directory.CreateDirectory("image");
    DirectoryInfo DirectoryImageOld =
        Directory.CreateDirectory("image_old");



    public Blockchain(
      Header headerGenesis,
      byte[] genesisBlockBytes,
      List<HeaderLocation> checkpoints)
    {
      HeaderGenesis = headerGenesis;
      HeaderTip = headerGenesis;

      Checkpoints = checkpoints;

      Branch = new BranchInserter(this);
      
      HeaderIndex = new Dictionary<int, List<Header>>();
      UpdateHeaderIndex(headerGenesis);
      
      UTXOTable = new UTXOTable(genesisBlockBytes);
    }
        

    public async Task Start()
    {
      LoadImage(0);

      await LoadBlocks();

      StartPeerGenerator();
      StartPeerSynchronizer();

      StartPeerInboundListener();
    }

    void LoadImage()
    {
      LoadImage(0);
    }
    void LoadImage(int stopHeight)
    {
      string pathImage = DirectoryImage.Name;

    LABEL_LoadImagePath:

      if(!TryLoadImagePath(pathImage) || 
        (Height > stopHeight && stopHeight > 0))
      {
        Initialize();

        if (pathImage == DirectoryImage.Name)
        {
          pathImage = DirectoryImageOld.Name;
          goto LABEL_LoadImagePath;
        }
      }
    }

    void Initialize()
    {
      HeaderTip = HeaderGenesis;
      Height = 0;
      Difficulty = HeaderGenesis.Difficulty;

      Branch.Initialize();

      HeaderIndex.Clear();
      UpdateHeaderIndex(HeaderGenesis);

      UTXOTable.Clear();
    }

    bool TryLoadImagePath(string pathImage)
    {
      try
      {
        var blockArchive = new UTXOTable.BlockArchive();

        string pathHeaderchain = Path.Combine(
          pathImage,
          "ImageHeaderchain");

        blockArchive.Buffer = File.ReadAllBytes(pathHeaderchain);

        blockArchive.Parse();

        Header header = blockArchive.HeaderRoot;

        if (!header.HashPrevious.IsEqual(HeaderGenesis.Hash))
        {
          throw new ChainException(
            "Header image does not link to genesis header.");
        }

        header.HeaderPrevious = HeaderGenesis;

        ValidateHeaders(header, 1);

        InsertHeaders(blockArchive);

        byte[] indexBlockArchiveBytes = File.ReadAllBytes(
          Path.Combine(pathImage, nameof(IndexBlockArchive)));

        IndexBlockArchive = BitConverter.ToInt32(
          indexBlockArchiveBytes, 0);

        UTXOTable.LoadImage(pathImage);

        LoadMapBlockToArchiveData(
          File.ReadAllBytes(
            Path.Combine(pathImage, "MapBlockHeader")));

        return true;
      }
      catch
      {
        return false;
      }
    }


    void LoadMapBlockToArchiveData(byte[] buffer)
    {
      int index = 0;

      while (index < buffer.Length)
      {
        byte[] key = new byte[32];
        Array.Copy(buffer, index, key, 0, 32);
        index += 32;

        int value = BitConverter.ToInt32(buffer, index);
        index += 4;

        MapBlockToArchiveIndex.Add(key, value);
      }
    }

    void ValidateHeaders(Header header, int height)
    {
      do
      {
        ValidateHeader(header, height);
        header = header.HeaderNext;
        height += 1;
      } while (header != null);
    }
    
    void InsertHeaders(
      UTXOTable.BlockArchive archiveBlock)
    {
      HeaderTip.HeaderNext = archiveBlock.HeaderRoot;

      HeaderTip = archiveBlock.HeaderTip;
      Difficulty += archiveBlock.Difficulty;
      Height += archiveBlock.Height;
    }


    byte[] HashStopLoading;
    const int COUNT_INDEXER_TASKS = 8;

    async Task LoadBlocks()
    {
      await LoadBlocks(new byte[32]);
    }
    async Task LoadBlocks(byte[] stopHashLoading)
    {
      IndexBlockArchiveLoad = IndexBlockArchive + 1;
      byte[] HashStopLoading = stopHashLoading;

      var loaderTasks = new Task[COUNT_INDEXER_TASKS];

      Parallel.For(
        0,
        COUNT_INDEXER_TASKS,
        i => loaderTasks[i] = StartLoader());

      await Task.WhenAll(loaderTasks);
    }

    async Task StartLoader()
    {
      UTXOTable.BlockArchive blockArchive = null;
      
    LABEL_LoadBlockArchive:

      while (true)
      {
        LoadBlockArchive(ref blockArchive);

        try
        {
          blockArchive.Buffer = File.ReadAllBytes(
          Path.Combine(
            ArchiveDirectoryBlocks.Name,
            blockArchive.Index.ToString()));

          blockArchive.Parse(HashStopLoading);
        }
        catch
        {
          blockArchive.IsInvalid = true;
        }

        while (true)
        {
          if (IsBlockLoadingCompleted)
          {
            return;
          }

          lock (LOCK_QueueBlockArchives)
          {
            if (blockArchive.Index == IndexBlockArchive + 1)
            {
              break;
            }

            if (QueueBlockArchives.Count < 10)
            {
              QueueBlockArchives.Add(
                blockArchive.Index,
                blockArchive);

              if(blockArchive.IsInvalid)
              {
                return;
              }

              blockArchive = null;
              goto LABEL_LoadBlockArchive;
            }
          }

          await Task.Delay(2000).ConfigureAwait(false);
        }
        
        try
        {
          while (!blockArchive.IsInvalid)
          {
            Header header = blockArchive.HeaderRoot;

            if (!header.HashPrevious.IsEqual(HeaderTip.Hash))
            {
              throw new ChainException(
                "Received header does not link to last header.");
            }

            header.HeaderPrevious = HeaderTip;

            ValidateHeaders(header, Height + 1);

            UTXOTable.InsertBlockArchive(blockArchive);

            InsertHeaders(blockArchive);

            if(HeaderTip.Hash.IsEqual(HashStopLoading))
            {
              IsBlockLoadingCompleted = true;
              return;
            }

            if (blockArchive.Index % UTXOIMAGE_INTERVAL_SYNC == 0)
            {
              CreateImage();
            }

            IndexBlockArchive += 1;

            lock (LOCK_QueueBlockArchives)
            {
              if (QueueBlockArchives.TryGetValue(
                IndexBlockArchive, 
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

      blockArchive.IndexBuffer = 0;
    }



    bool IsBlockLoadingCompleted;
    readonly object LOCK_QueueBlockArchives = new object();
    Dictionary<int, UTXOTable.BlockArchive> QueueBlockArchives =
      new Dictionary<int, UTXOTable.BlockArchive>();
    
    void ValidateHeader(Header header, int height)
    {
      HeaderLocation checkpoint =
        Checkpoints.Find(c => c.Height == height);
      if (
        checkpoint != null &&
        !checkpoint.Hash.IsEqual(header.Hash))
      {
        throw new ChainException(
          string.Format(
            "Header {0} at hight {1} not equal to checkpoint hash {2}",
            header.Hash.ToHexString(),
            height,
            checkpoint.Hash.ToHexString()),
          ErrorCode.INVALID);
      }

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

      uint targetBits = TargetManager.GetNextTargetBits(
          header.HeaderPrevious,
          (uint)height);

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
      Peer peer;

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

    async Task SynchronizeWithPeer(Peer peer)
    {
    LABEL_StageBranch:

      await Branch.Stage(peer);

      if (Branch.Difficulty > Difficulty)
      {
        if (Branch.IsFork)
        {
          LoadImage(Branch.HeightAncestor);
                              
          await LoadBlocks(Branch.HeaderRoot.HashPrevious);

          if (Height != Branch.HeightAncestor)
          {
            goto LABEL_StageBranch;
          }
        }

        StartUTXOSyncSessions(Branch.HeaderRoot);

        await RunUTXOInserter();

        if (Branch.DifficultyInserted > Difficulty)
        {
          Branch.Commit();
        }
        else
        {
          if (Branch.IsFork)
          {
            LoadImage();

            ArchiveDirectoryHeadersFork.EnumerateFiles().ToList()
              .ForEach(f => f.Delete());
            ArchiveDirectoryBlocksFork.EnumerateFiles().ToList()
              .ForEach(f => f.Delete());

            await LoadBlocks();
          }

          peer.Dispose();
        }
      }
      else if (Branch.Difficulty < Difficulty)
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

    async Task StartUTXOSyncSessions(Header headerRoot)
    {
      HeaderLoad = headerRoot;

      while (true)
      {
        var peersIdle = new List<Peer>();

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

    
    readonly object LOCK_HeaderLoad = new object();
    readonly object LOCK_BatchIndex = new object();
    int BatchIndex;

    async Task RunUTXOSyncSession(Peer peer)
    {
      if (peer.BlockArchives.Count == 0)
      {
        UTXOTable.BlockArchive blockArchive = null;
        LoadBlockArchive(ref blockArchive);

        lock (LOCK_HeaderLoad)
        {
          if (HeaderLoad == null)
          {
            FlagAllHeadersLoaded = true;
            peer.SetStatusIdle();
            return;
          }

          blockArchive.HeaderRoot = HeaderLoad;

          do
          {
            blockArchive.BlockCount += 1;
            blockArchive.Height += 1;
            blockArchive.Difficulty += HeaderLoad.Difficulty;
            blockArchive.HeaderTip = HeaderLoad;

            HeaderLoad = HeaderLoad.HeaderNext;
          } while (
             HeaderLoad != null &&
             blockArchive.BlockCount < peer.CountBlocksLoad);
        }
        
        while (!await peer.TryDownloadBlocks(blockArchive))
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
                  p.BlockArchives.Peek().Index > blockArchive.Index);

              if (peer != null)
              {
                peer.SetStatusBusy();
                break;
              }
            }

            await Task.Delay(1000);
          }
        }

        peer.BlockArchives.Push(blockArchive);

        peer.CalculateNewCountBlocks();
      }

      lock (LOCK_BatchIndex)
      {
        if (peer.BlockArchives.Peek().Index != BatchIndex)
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
          p.BlockArchives.Peek().Index == BatchIndex);
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
    BufferBlock<Peer> QueuePeersUTXOInserter =
      new BufferBlock<Peer>();

    async Task RunUTXOInserter()
    {
      while (true)
      {
        Peer peer = await QueuePeersUTXOInserter
          .ReceiveAsync()
          .ConfigureAwait(false);

        UTXOTable.BlockArchive blockArchive = 
          peer.BlockArchives.Pop();

        blockArchive.Index = IndexBlockArchive;

        try
        {
          UTXOTable.InsertBlockArchive(blockArchive);
        }
        catch (ChainException ex)
        {
          Console.WriteLine(
            "Exception when inserting block {1}: \n{2}",
            blockArchive.HeaderTip.Hash.ToHexString(),
            ex.Message);

          peer.Dispose();
          return;
        }

        Branch.InsertHeaders(blockArchive);

        if (Branch.IsFork)
        {
          if (Branch.DifficultyInserted > Difficulty)
          {
            Branch.IsFork = false;

            ArchiveDirectoryHeadersFork.EnumerateFiles().ToList()
            .ForEach(f => f.CopyTo(
              ArchiveDirectoryHeaders.FullName + f.Name,
              true));

            ArchiveDirectoryBlocksFork.EnumerateFiles().ToList()
            .ForEach(f => f.CopyTo(
              ArchiveDirectoryBlocks.FullName + f.Name,
              true));
          }
        }

        ArchiveBlock(blockArchive, UTXOIMAGE_INTERVAL_SYNC);

        if (blockArchive.IsCancellationBatch)
        {
          peer.SetStatusCompleted();
          return;
        }

        RunUTXOSyncSession(peer);
      }
    }


    int IndexBlockArchive;
    List<UTXOTable.BlockArchive> BlockArchives =
      new List<UTXOTable.BlockArchive>();
    int CountTXs;
    int SIZE_BLOCK_ARCHIVE = 50000;

    void ArchiveBlock(
      UTXOTable.BlockArchive blockArchive, 
      int intervalImage)
    {
      BlockArchives.Add(blockArchive);
      CountTXs += blockArchive.CountTX;

      if (CountTXs >= SIZE_BLOCK_ARCHIVE)
      {
        string directoryName = Branch.IsFork ?
          ArchiveDirectoryBlocksFork.FullName :
          ArchiveDirectoryBlocks.FullName;

        string pathFileArchive = Path.Combine(
          directoryName,
          IndexBlockArchive.ToString());

        var fileBlockArchive = new FileStream(
          pathFileArchive,
          FileMode.Create,
          FileAccess.Write,
          FileShare.None,
          bufferSize: 65536);

        BlockArchives.ForEach(
          c => WriteToFile(fileBlockArchive, c.Buffer));
        
        if (!Branch.IsFork &&
          IndexBlockArchive % intervalImage == 0)
        {
          CreateImage();
        }

        IndexBlockArchive += 1;

        BlockArchives.Clear();
        CountTXs = 0;
      }
    }

    public Dictionary<byte[], int> MapBlockToArchiveIndex =
      new Dictionary<byte[], int>(new EqualityComparerByteArray());

    void CreateImage()
    {
      DirectoryImageOld.Delete(true);
      DirectoryImage.MoveTo(DirectoryImageOld.Name);
      DirectoryImage.Create();

      string pathimageHeaderchain = Path.Combine(
        DirectoryImage.Name,
        "ImageHeaderchain");

      using (var fileImageHeaderchain = new FileStream(
        pathimageHeaderchain,
        FileMode.Create,
        FileAccess.Write,
        FileShare.None,
        bufferSize: 65536))
      {
        Header header = HeaderGenesis.HeaderNext;

        while (header != null)
        {
          byte[] headerBytes = header.GetBytes();

          fileImageHeaderchain.Write(
            headerBytes, 0, headerBytes.Length);

          header = header.HeaderNext;
        }
      }

      File.WriteAllBytes(
        Path.Combine(DirectoryImage.Name, nameof(IndexBlockArchive)), 
        BitConverter.GetBytes(IndexBlockArchive));

      using (FileStream stream = new FileStream(
         Path.Combine(DirectoryImage.Name, "MapBlockHeader"),
         FileMode.Create,
         FileAccess.Write))
      {
        foreach (KeyValuePair<byte[], int> keyValuePair
          in MapBlockToArchiveIndex)
        {
          stream.Write(
            keyValuePair.Key, 
            0, 
            keyValuePair.Key.Length);

          byte[] valueBytes = BitConverter.GetBytes(
            keyValuePair.Value);

          stream.Write(valueBytes, 0, valueBytes.Length);
        }
      }

      UTXOTable.CreateImage(DirectoryImage.Name);
    }

    static void WriteToFile(
      FileStream file,
      byte[] bytes)
    {
      while (true)
      {
        try
        {
          file.Write(bytes, 0, bytes.Length);
          break;
        }
        catch (IOException ex)
        {
          Console.WriteLine(
            ex.GetType().Name + ": " + ex.Message);

          Thread.Sleep(2000);
          continue;
        }
      }
    }

    

    async Task ReceiveHeader(byte[] headerBytes, Peer peer)
    {
      // Code im peer ausführen
      UTXOTable.BlockArchive blockArchive = null;
      LoadBlockArchive(ref blockArchive);

      blockArchive.Buffer = headerBytes;
      
      blockArchive.Parse();

      int countLockTriesRemaining = 20;
      while (true)
      {
        lock (LOCK_IsBlockchainLocked)
        {
          if (IsBlockchainLocked)
          {
            if (countLockTriesRemaining == 0)
            {
              Console.WriteLine("Server overloaded.");
              return;
            }

            countLockTriesRemaining -= 1;
          }
          else
          {
            IsBlockchainLocked = true;
            break;
          }
        }

        await Task.Delay(250);
      }

      Header header = blockArchive.HeaderRoot;

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
        if(!await peer.TryDownloadBlocks(blockArchive))
        {
          return;
        }
                
        blockArchive.Index = IndexBlockArchive;

        UTXOTable.InsertBlockArchive(blockArchive);

        InsertHeaders(blockArchive);

        ArchiveBlock(blockArchive, UTXOIMAGE_INTERVAL_LISTEN);
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


    static List<byte[]> GetLocatorHashes(Header header)
    {
      var locator = new List<byte[]>();
      int depth = 0;
      int nextLocationDepth = 0;

      while (header.HeaderPrevious != null)
      {
        if (depth == nextLocationDepth)
        {
          locator.Add(header.Hash);

          nextLocationDepth = 2 * nextLocationDepth + 1;
        }

        depth++;
        header = header.HeaderPrevious;
      }

      locator.Add(header.Hash);

      return locator;
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


    const int COUNT_PEERS_MAX = 4;
    TcpListener TcpListener = new TcpListener(IPAddress.Any, Port);
    const UInt16 Port = 8333;
    object LOCK_Peers = new object();
    List<Peer> Peers = new List<Peer>();

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
          var peer = await CreatePeer();

          lock (LOCK_Peers)
          {
            Peers.Add(peer);
          }

          continue;
        }

        await Task.Delay(2000);
      }
    }

    async Task<Peer> CreatePeer()
    {
      while (true)
      {
        IPAddress iPAddress;

        try
        {
          iPAddress = await GetNodeAddress();
        }
        catch
        {
          Console.WriteLine(
            "Cannot create peer: No node address available.");
          Task.Delay(10000);
          continue;
        }

        var peer = new Peer(this);

        try
        {
          await peer.Connect(iPAddress, Port);
        }
        catch
        {
          peer.Dispose();

          Task.Delay(10000);
          continue;
        }

        peer.Run();

        return peer;
      }
    }

    static async Task<IPAddress> GetNodeAddress()
    {
      while (true)
      {
        lock (LOCK_IsAddressPoolLocked)
        {
          if (!IsAddressPoolLocked)
          {
            IsAddressPoolLocked = true;
            break;
          }
        }

        await Task.Delay(1000);
      }

      if (SeedNodeIPAddresses.Count == 0)
      {
        DownloadIPAddressesFromSeeds();
      }

      int randomIndex = RandomGenerator
        .Next(SeedNodeIPAddresses.Count);

      IPAddress iPAddress = SeedNodeIPAddresses[randomIndex];
      SeedNodeIPAddresses.Remove(iPAddress);

      lock (LOCK_IsAddressPoolLocked)
      {
        IsAddressPoolLocked = false;
      }

      return iPAddress;
    }

    static readonly object LOCK_IsAddressPoolLocked = new object();
    static bool IsAddressPoolLocked;
    static List<IPAddress> SeedNodeIPAddresses = new List<IPAddress>();
    static Random RandomGenerator = new Random();


    static void DownloadIPAddressesFromSeeds()
    {
      string[] dnsSeeds = File.ReadAllLines(@"..\..\DNSSeeds");

      foreach (string dnsSeed in dnsSeeds)
      {
        if (dnsSeed.Substring(0, 2) == "//")
        {
          continue;
        }

        IPHostEntry iPHostEntry = Dns.GetHostEntry(dnsSeed);

        SeedNodeIPAddresses.AddRange(iPHostEntry.AddressList
          .Where(a => a.AddressFamily == AddressFamily.InterNetwork));
      }

      if (SeedNodeIPAddresses.Count == 0)
      {
        throw new ChainException("No seed addresses downloaded.");
      }
    }



    const int PEERS_COUNT_INBOUND = 8;

    async Task StartPeerInboundListener()
    {
      TcpListener.Start(PEERS_COUNT_INBOUND);

      while (true)
      {
        TcpClient tcpClient = await TcpListener.AcceptTcpClientAsync().
          ConfigureAwait(false);

        Console.WriteLine("Received inbound request from {0}",
          tcpClient.Client.RemoteEndPoint.ToString());

        var peer = new Peer(tcpClient, this);

        peer.Run();

        lock (LOCK_Peers)
        {
          Peers.Add(peer);
        }
      }
    }
  }
}
