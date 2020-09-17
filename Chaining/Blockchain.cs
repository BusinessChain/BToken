using System;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using System.Threading.Tasks;
using System.Threading.Tasks.Dataflow;
using System.Security.Cryptography;
using System.Net;
using System.Net.Sockets;
using System.Diagnostics;



namespace BToken.Chaining
{
  partial class Blockchain
  {
    Dictionary<int, byte[]> Checkpoints;

    Header HeaderGenesis;
    Header HeaderTip;
    double Difficulty;
    int Height;

    readonly object HeaderIndexLOCK = new object();
    Dictionary<int, List<Header>> HeaderIndex;
    
    UTXOTable UTXOTable;
    const int UTXOIMAGE_INTERVAL_SYNC = 500;
    const int UTXOIMAGE_INTERVAL_LISTEN = 50;
    int SIZE_BLOCK_ARCHIVE = 2500;

    readonly object LOCK_IsBlockchainLocked = new object();
    bool IsBlockchainLocked;
    readonly object LOCK_IndexBlockArchiveLoad = new object();
    int IndexBlockArchiveLoad;

    DirectoryInfo ArchiveDirectoryBlocks =
        Directory.CreateDirectory("J:\\BlockArchivePartitioned");

    DirectoryInfo DirectoryImage =
        Directory.CreateDirectory("image");
    DirectoryInfo DirectoryImageOld =
        Directory.CreateDirectory("image_old");



    public Blockchain(
      Header headerGenesis,
      byte[] genesisBlockBytes,
      Dictionary<int, byte[]> checkpoints)
    {
      HeaderGenesis = headerGenesis;
      HeaderTip = headerGenesis;

      Checkpoints = checkpoints;
            
      HeaderIndex = new Dictionary<int, List<Header>>();
      UpdateHeaderIndex(headerGenesis);
      
      UTXOTable = new UTXOTable(genesisBlockBytes);
    }
        


    public async Task Start()
    {
      await LoadImage();

      StartPeerGenerator();

      StartPeerSynchronizer();

      // StartPeerInboundListener();
    }



    async Task LoadImage()
    {
      await LoadImage(0, new byte[32]);
    }

    async Task LoadImage(
      int heightMax, 
      byte[] stopHashLoading)
    {
      string pathImage = DirectoryImage.Name;

    LABEL_LoadImagePath:

      if(!TryLoadImage(pathImage) || 
        (Height > heightMax && heightMax > 0))
      {
        Initialize();

        if (pathImage == DirectoryImage.Name)
        {
          pathImage = DirectoryImageOld.Name;
          goto LABEL_LoadImagePath;
        }
      }
       
      await LoadBlocks(stopHashLoading);
    }

    void Initialize()
    {
      HeaderTip = HeaderGenesis;
      Height = 0;
      Difficulty = HeaderGenesis.Difficulty;
      
      HeaderIndex.Clear();
      UpdateHeaderIndex(HeaderGenesis);

      UTXOTable.Clear();
    }

    bool TryLoadImage(string pathImage)
    {
      try
      {
        LoadImageHeaderchain(pathImage);

        IndexBlockArchive = BitConverter.ToInt32(
          File.ReadAllBytes(
            Path.Combine(
              pathImage, 
              nameof(IndexBlockArchive))), 
          0);

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

    void LoadImageHeaderchain(string pathImage)
    {
      string pathFile = Path.Combine(
        pathImage,
        "ImageHeaderchain");

      var blockArchive = new UTXOTable.BlockArchive();

      blockArchive.Parse(File.ReadAllBytes(pathFile));

      Header header = blockArchive.HeaderRoot;

      if (!header.HashPrevious.IsEqual(
        HeaderGenesis.Hash))
      {
        throw new ChainException(
          "Header image does not link to genesis header.");
      }

      header.HeaderPrevious = HeaderGenesis;

      ValidateHeaders(header, 1);

      InsertHeaders(blockArchive);
    }



    byte[] HashStopLoading;
    const int COUNT_LOADER_TASKS = 1;
    
    async Task LoadBlocks(byte[] stopHashLoading)
    {
      IndexBlockArchiveLoad = IndexBlockArchive;
      HashStopLoading = stopHashLoading;

      var loaderTasks = new Task[COUNT_LOADER_TASKS];

      Parallel.For(
        0,
        COUNT_LOADER_TASKS,
        i => loaderTasks[i] = StartLoader());

      await Task.WhenAll(loaderTasks);
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


    readonly object LOCK_QueueBlockArchives = new object();
    readonly object LOCK_IndexBlockArchive = new object();

    async Task StartLoader()
    {
      UTXOTable.BlockArchive blockArchive = null;

    LABEL_LoadBlockArchive:

      LoadBlockArchive(ref blockArchive);

      blockArchive.Reset();

      try
      {
        string pathFile = Path.Combine(
          ArchiveDirectoryBlocks.FullName,
          blockArchive.Index.ToString());

        blockArchive.Parse(
          File.ReadAllBytes(pathFile),
          HashStopLoading);
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

        lock (LOCK_IndexBlockArchive)
        {
          if (blockArchive.Index == IndexBlockArchive)
          {
            break;
          }
        }

        lock (LOCK_QueueBlockArchives)
        {
          if (QueueBlockArchives.Count < 10)
          {
            QueueBlockArchives.Add(
              blockArchive.Index,
              blockArchive);

            if (blockArchive.IsInvalid)
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
          
          if (blockArchive.CountTX < SIZE_BLOCK_ARCHIVE)
          {
            CountTXsArchive = blockArchive.CountTX;
            OpenBlockArchive(IndexBlockArchive);

            IsBlockLoadingCompleted = true;
            return;
          }

          if (HeaderTip.Hash.IsEqual(HashStopLoading))
          {
            break;
          }

          if ((blockArchive.Index + 1) %
            UTXOIMAGE_INTERVAL_SYNC == 0)
          {
            CreateImage();
          }
          
          lock (LOCK_IndexBlockArchive)
          {
            IndexBlockArchive += 1;
          }

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
              goto LABEL_LoadBlockArchive;
            }
          }
        }
      }
      catch (ChainException)
      {
        File.Delete(
          Path.Combine(
            ArchiveDirectoryBlocks.Name,
            blockArchive.Index.ToString()));
      }

      CountTXsArchive = 0;
      CreateBlockArchive(IndexBlockArchive + 1);

      IsBlockLoadingCompleted = true;
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
    Dictionary<int, UTXOTable.BlockArchive> QueueBlockArchives =
      new Dictionary<int, UTXOTable.BlockArchive>();
    
    void ValidateHeader(Header header, int height)
    {
      if (
        Checkpoints.TryGetValue(
          height, 
          out byte[] hashCheckpoint) &&
        !hashCheckpoint.IsEqual(header.Hash))
      {
        throw new ChainException(
          string.Format(
            "Header {0} at hight {1} not equal to checkpoint hash {2}",
            header.Hash.ToHexString(),
            height,
            hashCheckpoint.ToHexString()),
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
            "In header {0}\n nBits {1} not equal to target nBits {2}",
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

        Console.WriteLine(
          "Synchronize with peer {0}", 
          peer.GetIdentification());

        peer.SetStatusBusy();

        await SynchronizeWithPeer(peer);

        peer.IsSynchronized = true;

        IsBlockchainLocked = false;
      }
    }

    async Task SynchronizeWithPeer(Peer peer)
    {
    LABEL_StageBranch:

      List<Header> locator = GetLocator();

      try
      {
        Header headerRoot = await peer.GetHeaders(locator);
        
        if (headerRoot == null)
        {
          return;
        }

        if (headerRoot.HeaderPrevious == HeaderTip)
        {
          await peer.BuildHeaderchain(
            headerRoot, 
            Height + 1);

          try
          {
            await SynchronizeUTXO(headerRoot, peer);
          }
          catch(Exception ex)
          {
            peer.Dispose(string.Format(
              "Exception {0} when syncing with peer {1}: \n{2}",
              ex.GetType(),
              peer.GetIdentification(),
              ex.Message));

            await LoadImage();
          }

          return;
        }

        headerRoot = await peer.SkipDuplicates(
          headerRoot, 
          locator);

        int heightAncestor = Height - 1;
        double difficultyAncestor = Difficulty - HeaderTip.Difficulty;

        Header header = HeaderTip.HeaderPrevious;

        while (header != headerRoot.HeaderPrevious)
        {
          difficultyAncestor -= header.Difficulty;
          heightAncestor -= 1;
          header = header.HeaderPrevious;
        }

        double difficultyFork = difficultyAncestor +
          await peer.BuildHeaderchain(
            headerRoot,
            heightAncestor + 1);

        if (difficultyFork > Difficulty)
        {
          double difficultyOld = Difficulty;

          await LoadImage(
            heightAncestor, 
            headerRoot.HashPrevious);

          if (Height != heightAncestor)
          {
            goto LABEL_StageBranch;
          }

          try
          {
            await SynchronizeUTXO(headerRoot, peer);
          }
          catch(Exception ex)
          {
            peer.Dispose(string.Format(
              "Exception {0} when syncing with peer {1}: \n{2}",
              ex.GetType(),
              peer.GetIdentification(),
              ex.Message));

            await LoadImage(
              heightAncestor,
              headerRoot.HashPrevious);

            return;
          }

          if (!(Difficulty > difficultyOld))
          {
            await LoadImage(
              heightAncestor,
              headerRoot.HashPrevious);
          }
        }
        else if (difficultyFork < Difficulty)
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
      catch (Exception ex)
      {
        peer.Dispose(string.Format(
          "Exception {0} when syncing with peer {1}: \n{2}",
          ex.GetType(),
          peer.GetIdentification(),
          ex.Message));
      }
    }
    
    List<Header> GetLocator()
    {
      Header header = HeaderTip;
      var locator = new List<Header>();
      int height = Height;
      int heightCheckpoint = Checkpoints.Keys.Max();
      int depth = 0;
      int nextLocationDepth = 0;

      while (height > heightCheckpoint)
      {
        if (depth == nextLocationDepth)
        {
          locator.Add(header);
          nextLocationDepth = 2 * nextLocationDepth + 1;
        }

        depth++;
        height--;
        header = header.HeaderPrevious;
      }

      locator.Add(header);

      return locator;
    }
       
    Header HeaderLoad;
    BufferBlock<Peer> QueueSynchronizer =
      new BufferBlock<Peer>();

    async Task SynchronizeUTXO(
      Header headerRoot,
      Peer peerSynchronizing)
    {
      HeaderLoad = headerRoot;

      Task taskUTXOSyncSessions = 
        StartUTXOSyncSessions(peerSynchronizing);
      
      while (true)
      {
        Peer peer = await QueueSynchronizer.ReceiveAsync()
          .ConfigureAwait(false);

        UTXOTable.BlockArchive blockArchive =
          peer.BlockArchivesDownloaded.Pop();

        blockArchive.Index = IndexBlockArchive;

        UTXOTable.InsertBlockArchive(blockArchive);

        InsertHeaders(blockArchive);

        ArchiveBlock(blockArchive, UTXOIMAGE_INTERVAL_SYNC);

        if (blockArchive.IsLastArchive)
        {
          peer.SetUTXOSyncComplete();
          await taskUTXOSyncSessions;
          return;
        }

        RunUTXOSyncSession(peer);
      }
    }


    
    async Task StartUTXOSyncSessions(Peer peerSynchronizing)
    {
      RunUTXOSyncSession(peerSynchronizing);

      while (true)
      {
        lock (LOCK_HeaderLoad)
        {
          if (Peers.All(p => p.IsUTXOSyncComplete()))
          {
            return;
          }
        }

        var peersIdle = new List<Peer>();

        peersIdle = Peers.FindAll(p => p.IsStatusIdle());
        peersIdle.ForEach(p => p.SetStatusBusy());
        peersIdle.Select(p => RunUTXOSyncSession(p)).ToList();

        await Task.Delay(1000).ConfigureAwait(false);
      }
    }

    
    readonly object LOCK_HeaderLoad = new object();
    readonly object LOCK_IndexBlockArchiveQueue = new object();
    int IndexBlockArchiveDownload;
    int IndexBlockArchiveQueue;

    async Task RunUTXOSyncSession(Peer peer)
    {
      if (peer.BlockArchivesDownloaded.Count == 0)
      {
        lock (LOCK_HeaderLoad)
        {
          peer.BlockArchive.Index = IndexBlockArchiveDownload;
          IndexBlockArchiveDownload += 1;

          if (HeaderLoad == null)
          {
            peer.SetStatusIdle();
            return;
          }

          peer.CreateInventories(ref HeaderLoad);
        }
        
        while (!await peer.TryDownloadBlocks())
        {
          while (true)
          {
            lock (LOCK_Peers)
            {
              if (Peers.All(p => p.IsUTXOSyncComplete()))
              {
                return;
              }

              peer = 
                Peers.Find(p =>
                p.IsStatusAwaitingInsertion() &&
                p.BlockArchivesDownloaded.Peek().Index > 
                peer.BlockArchive.Index);

              if (peer != null)
              {
                peer.SetStatusBusy();
                break;
              }
            }

            await Task.Delay(1000)
              .ConfigureAwait(false);
          }
        }
      }

      lock (LOCK_IndexBlockArchiveQueue)
      {
        if (peer.BlockArchivesDownloaded.Peek().Index != 
          IndexBlockArchiveQueue)
        {
          peer.SetStatusAwaitingInsertion();
          return;
        }
      }

      while (true)
      {
        QueueSynchronizer.Post(peer);

        lock (LOCK_IndexBlockArchiveQueue)
        {
          IndexBlockArchiveQueue += 1;
        }

        lock (LOCK_Peers)
        {
          peer = Peers.Find(p =>
          p.IsStatusAwaitingInsertion() &&
          p.BlockArchivesDownloaded.Peek().Index == IndexBlockArchiveQueue);

          if (peer == null)
          {
            return;
          }

          peer.SetStatusBusy();
        }
      }
    }


         
    int IndexBlockArchive;
    List<UTXOTable.BlockArchive> BlockArchives =
      new List<UTXOTable.BlockArchive>();
    int CountTXsArchive;
    FileStream FileBlockArchive;
    
    void ArchiveBlock(
      UTXOTable.BlockArchive blockArchive, 
      int intervalImage)
    {
      FileBlockArchive.Write(
        blockArchive.Buffer,
        0,
        blockArchive.IndexBuffer);

      FileBlockArchive.Flush();

      CountTXsArchive += blockArchive.CountTX;

      if (CountTXsArchive >= SIZE_BLOCK_ARCHIVE)
      {
        FileBlockArchive.Dispose();

        CountTXsArchive = 0;

        IndexBlockArchive += 1;

        if (IndexBlockArchive % intervalImage == 0)
        {
          CreateImage();
        }

        CreateBlockArchive(IndexBlockArchive);

        return;
      }
    }

    void OpenBlockArchive(int indexArchive)
    {
      string pathFileArchive = Path.Combine(
        ArchiveDirectoryBlocks.FullName,
        indexArchive.ToString());

      FileBlockArchive = new FileStream(
       pathFileArchive,
       FileMode.Append,
       FileAccess.Write,
       FileShare.None,
       bufferSize: 65536);
    }

    void CreateBlockArchive(int indexArchive)
    {
      string pathFileArchive = Path.Combine(
        ArchiveDirectoryBlocks.FullName,
        indexArchive.ToString());

      FileBlockArchive = new FileStream(
       pathFileArchive,
       FileMode.Create,
       FileAccess.Write,
       FileShare.None,
       bufferSize: 65536);
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
        Path.Combine(
          DirectoryImage.Name, 
          nameof(IndexBlockArchive)),
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
    
    

    async Task ReceiveHeader(byte[] headerBytes, Peer peer)
    {
      UTXOTable.BlockArchive blockArchive = null;
      LoadBlockArchive(ref blockArchive);
            
      blockArchive.Parse(headerBytes,0, headerBytes.Length);

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
             
        var headerDuplicates = new List<byte[]>();
        int depthDuplicateAcceptedMax = 3;
        int depthDuplicate = 0;

        while (depthDuplicate < depthDuplicateAcceptedMax)
        {
          if (headerContained.Hash.IsEqual(header.Hash))
          {
            if (headerDuplicates.Any(h => h.IsEqual(header.Hash)))
            {
              throw new ChainException(
                string.Format(
                  "Received duplicate header {0} more than once.",
                  header.Hash.ToHexString()));
            }

            headerDuplicates.Add(header.Hash);
            if (headerDuplicates.Count > depthDuplicateAcceptedMax)
            {
              headerDuplicates = headerDuplicates.Skip(1)
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
        ValidateHeader(header, Height + 1);

        if (!await peer.TryDownloadBlocks())
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

        peer.IsSynchronized = true;

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


    const int COUNT_PEERS_MAX = 1;
    TcpListener TcpListener = 
      new TcpListener(IPAddress.Any, Port);
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

          Console.WriteLine("created peer {0}", 
            peer.GetIdentification());

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
        catch(Exception ex)
        {
          Console.WriteLine(
            "Cannot get peer address from dns server: {0}",
            ex.Message);

          Task.Delay(5000);
          continue;
        }

        var peer = new Peer(this, iPAddress);
        
        try
        {
          await peer.Connect();
        }
        catch(Exception ex)
        {
          peer.Dispose(ex.Message);

          Task.Delay(5000);
          continue;
        }

        peer.StartMessageListener();

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
      try
      {
        string[] dnsSeeds = File.ReadAllLines(@"..\..\DNSSeeds");

        foreach (string dnsSeed in dnsSeeds)
        {
          if (dnsSeed.Substring(0, 2) == "//")
          {
            continue;
          }

          IPHostEntry iPHostEntry = Dns.GetHostEntry(dnsSeed);

          SeedNodeIPAddresses.AddRange(iPHostEntry.AddressList);
        }
      }
      catch
      {
        if (SeedNodeIPAddresses.Count == 0)
        {
          throw new ChainException("No seed addresses downloaded.");
        }
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

        peer.StartMessageListener();

        lock (LOCK_Peers)
        {
          Peers.Add(peer);
        }
      }
    }
  }
}
