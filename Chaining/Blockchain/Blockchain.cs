using System;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Security.Cryptography;
using System.Threading.Tasks.Dataflow;



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

    BlockchainNetwork Network;
    
    string NameFork = "Fork";
    string NameImage = "Image";
    string NameOld = "Old";

    string FileNameIndexBlockArchiveImage = "IndexBlockArchive";
    string PathBlockArchive = "J:\\BlockArchivePartitioned";
    string PathBlockArchiveFork = "J:\\BlockArchivePartitionedFork";

    readonly object LOCK_IsBlockchainLocked = new object();
    bool IsBlockchainLocked;
    
    long UTCTimeStartMerger = 
      DateTimeOffset.UtcNow.ToUnixTimeSeconds();

         

    public int IndexBlockArchive;

    byte[] HashRootFork;
    public const int COUNT_LOADER_TASKS = 4;
    int SIZE_BLOCK_ARCHIVE = 20000;
    const int UTXOIMAGE_INTERVAL_LOADER = 500;

    readonly object LOCK_IndexBlockArchiveQueue = new object();
    int IndexBlockArchiveQueue;
            
    StreamWriter LogFile;

    readonly object LOCK_IndexBlockArchiveLoad = new object();
    int IndexBlockArchiveLoad;




    public Blockchain(
      Header headerGenesis,
      byte[] genesisBlockBytes,
      Dictionary<int, byte[]> checkpoints)
    {
      Network = new BlockchainNetwork(this);

      HeaderGenesis = headerGenesis;
      HeaderTip = headerGenesis;

      Checkpoints = checkpoints;
            
      HeaderIndex = new Dictionary<int, List<Header>>();
      UpdateHeaderIndex(headerGenesis);
      
      UTXOTable = new UTXOTable(genesisBlockBytes);

      DirectoryInfo DirectoryBlockArchive =
          Directory.CreateDirectory(PathBlockArchive);

      LogFile = new StreamWriter("logArchiver", false);
    }



    public async Task Start()
    {
      await LoadImage();

      Network.Start();
    }



    async Task LoadImage()
    {
      await LoadImage(0, new byte[32]);
    }
        
    async Task LoadImage(
      int heightMax,
      byte[] hashRootFork)
    {
      string pathImage = NameImage;
      
      while(true)
      {
        Initialize();

        if (!TryLoadImageFile(
          pathImage, 
          out int indexBlockArchiveImage) ||
        (heightMax > 0 && Height > heightMax))
        {
          if (pathImage == NameImage)
          {
            pathImage += NameOld;

            continue;
          }

          Initialize();
        }
        
        if (await TryLoadBlocks(
          hashRootFork,
          indexBlockArchiveImage))
        {
          return;
        }
      }
    }


    bool TryLoadImageFile(
      string pathImage, 
      out int indexBlockArchiveImage)
    {
      try
      {
        LoadImageHeaderchain(pathImage);

        indexBlockArchiveImage = BitConverter.ToInt32(
          File.ReadAllBytes(
            Path.Combine(
              pathImage,
              FileNameIndexBlockArchiveImage)),
          0);

        UTXOTable.LoadImage(pathImage);

        LoadMapBlockToArchiveData(
          File.ReadAllBytes(
            Path.Combine(pathImage, "MapBlockHeader")));

        return true;
      }
      catch(Exception ex)
      {
        Console.WriteLine(ex.Message);

        indexBlockArchiveImage = 0;
        return false;
      }
    }

    void LoadImageHeaderchain(string pathImage)
    {
      string pathFile = Path.Combine(
        pathImage, "ImageHeaderchain");

      var blockParser = new UTXOTable.BlockParser();

      int indexBytesHeaderImage = 0;
      byte[] bytesHeaderImage = File.ReadAllBytes(pathFile);

      Header headerPrevious = HeaderGenesis;
      
      while(indexBytesHeaderImage < bytesHeaderImage.Length)
      {
        Header header = blockParser.ParseHeader(
         bytesHeaderImage,
         ref indexBytesHeaderImage);

        if (!header.HashPrevious.IsEqual(
          headerPrevious.Hash))
        {
          throw new ProtocolException(
            "Header image does not link to genesis header.");
        }

        header.HeaderPrevious = headerPrevious;

        ValidateHeader(
          header, 
          Height + 1);

        InsertHeader(header);

        headerPrevious = header;
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
        
    void Initialize()
    {
      HeaderTip = HeaderGenesis;
      Height = 0;
      Difficulty = HeaderGenesis.Difficulty;
      
      HeaderIndex.Clear();
      UpdateHeaderIndex(HeaderGenesis);

      UTXOTable.Clear();
    }
    
    void ValidateHeaders(Header header)
    {
      int height = Height + 1;

      do
      {
        ValidateHeader(header, height);
        header = header.HeaderNext;
        height += 1;
      } while (header != null);
    }

    void ValidateHeader(Header header, int height)
    {
      if (Checkpoints
        .TryGetValue(height, out byte[] hashCheckpoint) &&
        !hashCheckpoint.IsEqual(header.Hash))
      {
        throw new ProtocolException(
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
        throw new ProtocolException(
          string.Format(
            "Header {0} with unix time {1} " +
            "is older than median time past {2}.",
            header.Hash.ToHexString(),
            DateTimeOffset.FromUnixTimeSeconds(header.UnixTimeSeconds),
            DateTimeOffset.FromUnixTimeSeconds(medianTimePast)),
          ErrorCode.INVALID);
      }

      uint targetBitsNew;

      if ((height % RETARGETING_BLOCK_INTERVAL) == 0)
      {
        targetBitsNew = GetNextTarget(header.HeaderPrevious)
          .GetCompact();
      }
      else
      {
        targetBitsNew = header.NBits;
      }
      
      if (header.NBits != targetBitsNew)
      {
        throw new ProtocolException(
          string.Format(
            "In header {0}\n nBits {1} not equal to target nBits {2}",
            header.Hash.ToHexString(),
            header.NBits,
            targetBitsNew),
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

    const int RETARGETING_BLOCK_INTERVAL = 2016;
    const ulong RETARGETING_TIMESPAN_INTERVAL_SECONDS = 14 * 24 * 60 * 60;

    static readonly UInt256 DIFFICULTY_1_TARGET =
      new UInt256("00000000FFFF0000000000000000000000000000000000000000000000000000");


    static UInt256 GetNextTarget(Header header)
    {
      Header headerIntervalStart = header;
      int depth = RETARGETING_BLOCK_INTERVAL;

      while (--depth > 0 && headerIntervalStart.HeaderPrevious != null)
      {
        headerIntervalStart = headerIntervalStart.HeaderPrevious;
      }

      ulong actualTimespan = Limit(
        header.UnixTimeSeconds -
        headerIntervalStart.UnixTimeSeconds);

      UInt256 targetOld = UInt256.ParseFromCompact(header.NBits);

      UInt256 targetNew = targetOld
        .MultiplyBy(actualTimespan)
        .DivideBy(RETARGETING_TIMESPAN_INTERVAL_SECONDS);

      return UInt256.Min(DIFFICULTY_1_TARGET, targetNew);
    }

    static ulong Limit(ulong actualTimespan)
    {
      if (actualTimespan < RETARGETING_TIMESPAN_INTERVAL_SECONDS / 4)
      {
        return RETARGETING_TIMESPAN_INTERVAL_SECONDS / 4;
      }

      if (actualTimespan > RETARGETING_TIMESPAN_INTERVAL_SECONDS * 4)
      {
        return RETARGETING_TIMESPAN_INTERVAL_SECONDS * 4;
      }

      return actualTimespan;
    }

    void GetStateAtHeader(
      Header headerAncestor,
      out int heightAncestor,
      out double difficultyAncestor)
    {
      heightAncestor = Height - 1;
      difficultyAncestor = Difficulty - HeaderTip.Difficulty;

      Header header = HeaderTip.HeaderPrevious;

      while (header != headerAncestor)
      {
        difficultyAncestor -= header.Difficulty;
        heightAncestor -= 1;
        header = header.HeaderPrevious;
      }
    }

    
    public bool TryInsertBlock(
      Block block,
      bool flagValidateHeader)
    {
      try
      {
        block.Header.HeaderPrevious = HeaderTip;

        if (flagValidateHeader)
        {
          ValidateHeader(block.Header, Height + 1);
        }

        UTXOTable.InsertBlock(
          block,
          IndexBlockArchive);

        InsertHeader(block.Header);

        Console.WriteLine(
          "{0},{1},{2},{3}",
          Height,
          IndexBlockArchive,
          DateTimeOffset.UtcNow.ToUnixTimeSeconds() - UTCTimeStartMerger,
          UTXOTable.GetMetricsCSV());

        return true;
      }
      catch(Exception ex)
      {
        Console.WriteLine(
          "{0} when inserting block {1} in blockchain:\n {2}",
          ex.GetType().Name,
          block.Header.Hash.ToHexString(),
          ex.Message);

        return false;
      }
    }

    void InsertHeader(Header header)
    {
      HeaderTip.HeaderNext = header;
      HeaderTip = header;

      Difficulty += header.Difficulty;
      Height += 1;
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
          
                

    public Dictionary<byte[], int> MapBlockToArchiveIndex =
      new Dictionary<byte[], int>(
        new EqualityComparerByteArray());

       
    
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

      throw new ProtocolException(string.Format(
        "Locator does not root in headerchain."));
    }


    bool TryLock()
    {
      lock (LOCK_IsBlockchainLocked)
      {
        if (IsBlockchainLocked)
        {
          return false;
        }

        IsBlockchainLocked = true;

        return true;
      }
    }

    void ReleaseLock()
    {
      lock (LOCK_IsBlockchainLocked)
      {
        IsBlockchainLocked = false;
      }
    }



    public async Task<bool> TryLoadBlocks(
      byte[] hashRootFork,
      int indexBlockArchive)
    {
      "Start archive loader".Log(LogFile);

      IsInserterCompleted = false;

      IndexBlockArchiveLoad = indexBlockArchive;
      IndexBlockArchiveQueue = indexBlockArchive;
      HashRootFork = hashRootFork;

      Task inserterTask = RunLoaderInserter();

      var loaderTasks = new Task[COUNT_LOADER_TASKS];

      Parallel.For(
        0,
        COUNT_LOADER_TASKS,
        i => loaderTasks[i] = StartLoader());

      await inserterTask;

      IsInserterCompleted = true;

      await Task.WhenAll(loaderTasks);

      return IsInserterSuccess;
    }

    BufferBlock<BlockLoad> QueueLoader =
      new BufferBlock<BlockLoad>();
    bool IsInserterSuccess;

    async Task RunLoaderInserter()
    {
      "Start archive inserter.".Log(LogFile);

      IsInserterSuccess = true;

      while (true)
      {
        BlockLoad blockLoad = await QueueLoader
          .ReceiveAsync()
          .ConfigureAwait(false);

        IndexBlockArchive = blockLoad.Index;

        if (
          blockLoad.IsInvalid ||
          !HeaderTip.Hash.IsEqual(
            blockLoad.Blocks.First().Header.HashPrevious))
        {
          CreateBlockArchive();
          return;
        }

        foreach (Block block in blockLoad.Blocks)
        {
          if (!TryInsertBlock(
            block,
            flagValidateHeader: true))
          {
            File.Delete(
              Path.Combine(
                PathBlockArchive,
                blockLoad.Index.ToString()));

            IsInserterSuccess = false;
            return;
          }

          if (block.Header.Hash.IsEqual(HashRootFork))
          {
            FileBlockArchive.Dispose();

            CreateBlockArchive();

            foreach (Block blockArchiveFork in blockLoad.Blocks)
            {
              ArchiveBlock(blockArchiveFork, -1);

              if(blockArchiveFork == block)
              {
                return;
              }
            }
          }
        }

        if (blockLoad.CountTX < SIZE_BLOCK_ARCHIVE)
        {
          CountTXsArchive = blockLoad.CountTX;
          OpenBlockArchive();

          return;
        }

        IndexBlockArchive += 1;
        
        if (
          IndexBlockArchive % UTXOIMAGE_INTERVAL_LOADER == 0)
        {
          CreateImage(
            IndexBlockArchive,
            NameImage);
        }
      }
    }

    int CountTXsArchive;
    FileStream FileBlockArchive;

    public void ArchiveBlock(
      Block block,
      int intervalImage)
    {
      while (true)
      {
        try
        {
          FileBlockArchive.Write(
            block.Buffer,
            0,
            block.Buffer.Length);

          FileBlockArchive.Flush();

          break;
        }
        catch (Exception ex)
        {
          string.Format(
            "{0} when writing block {1} to " +
            "file {2}: \n{3} \n" +
            "Try again in 10 seconds ...",
            ex.GetType().Name,
            block.Header.Hash.ToHexString(),
            FileBlockArchive.Name,
            ex.Message)
            .Log(LogFile);

          Thread.Sleep(10000);
        }
      }

      CountTXsArchive += block.TXs.Count;

      if (CountTXsArchive >= SIZE_BLOCK_ARCHIVE)
      {
        FileBlockArchive.Dispose();

        IndexBlockArchive += 1;

        if (IndexBlockArchive % intervalImage == 0)
        {
          string pathImage = IsFork ? 
            Path.Combine(NameFork, NameImage) : 
            NameImage;

          CreateImage(
            IndexBlockArchive, 
            pathImage);
        }

        CreateBlockArchive();
      }
    }
    
    void OpenBlockArchive()
    {
      string.Format(
        "Open BlockArchive {0}",
        IndexBlockArchive)
        .Log(LogFile);

      string pathFileArchive = Path.Combine(
        PathBlockArchive,
        IndexBlockArchive.ToString());

      FileBlockArchive = new FileStream(
       pathFileArchive,
       FileMode.Append,
       FileAccess.Write,
       FileShare.None,
       bufferSize: 65536);
    }

    void CreateImage(
      int indexBlockArchive,
      string pathImage)
    {
      string pathimageOld = pathImage + NameOld;

      try
      {
        while (true)
        {
          try
          {
            Directory.Delete(pathimageOld, true);

            break;
          }
          catch (DirectoryNotFoundException)
          {
            break;
          }
          catch (Exception ex)
          {
            Console.WriteLine(
              "Cannot delete directory old due to {0}:\n{1}",
              ex.GetType().Name,
              ex.Message);

            Thread.Sleep(3000);
          }
        }

        while (true)
        {
          try
          {
            Directory.Move(
              pathImage,
              pathimageOld);

            break;
          }
          catch (DirectoryNotFoundException)
          {
            break;
          }
          catch (Exception ex)
          {
            Console.WriteLine(
              "Cannot move new image to old due to {0}:\n{1}",
              ex.GetType().Name,
              ex.Message);

            Thread.Sleep(3000);
          }
        }

        Directory.CreateDirectory(pathImage);

        string pathimageHeaderchain = Path.Combine(
          pathImage,
          "ImageHeaderchain");

        using (var fileImageHeaderchain =
          new FileStream(
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
            pathImage,
            FileNameIndexBlockArchiveImage),
          BitConverter.GetBytes(indexBlockArchive));

        using (FileStream stream = new FileStream(
           Path.Combine(pathImage, "MapBlockHeader"),
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

        UTXOTable.CreateImage(pathImage);
      }
      catch (Exception ex)
      {
        Console.WriteLine(
          "{0}:\n{1}",
          ex.GetType().Name,
          ex.Message);
      }
    }

    void CreateBlockArchive()
    {
      string pathFileArchive = Path.Combine(
        PathBlockArchive,
        IsFork ? NameFork : "",
        IndexBlockArchive.ToString());

      FileBlockArchive = new FileStream(
       pathFileArchive,
       FileMode.Create,
       FileAccess.Write,
       FileShare.None,
       bufferSize: 65536);

      CountTXsArchive = 0;
    }


    bool IsFork;

    async Task<bool> TryFork(
      int heightAncestor, 
      byte[] hashAncestor)
    {
      IsFork = true;

      await LoadImage(
         heightAncestor,
         hashAncestor);

      if(Height == heightAncestor)
      {
        return true;
      }

      IsFork = false;
      return false;
    }

    void DismissFork()
    {
      IsFork = false;
    }

    void Reorganize()
    {
      string pathImageFork = Path.Combine(
        NameFork, 
        NameImage);

      if(Directory.Exists(pathImageFork))
      {
        while (true)
        {
          try
          {
            Directory.Delete(
              NameImage,
              true);

            Directory.Move(
              pathImageFork,
              NameImage);

            break;
          }
          catch (DirectoryNotFoundException)
          {
            break;
          }
          catch (Exception ex)
          {
            Console.WriteLine(
              "{0} when attempting to delete directory:\n{1}",
              ex.GetType().Name,
              ex.Message);

            Thread.Sleep(3000);
          }
        }

      }
      
      string pathImageForkOld = Path.Combine(
        NameFork,
        NameImage,
        NameOld);

      string pathImageOld = Path.Combine(
        NameImage,
        NameOld);

      if (Directory.Exists(pathImageForkOld))
      {
        while (true)
        {
          try
          {
            Directory.Delete(
              pathImageOld,
              true);

            Directory.Move(
              pathImageForkOld,
              pathImageOld);

            break;
          }
          catch (DirectoryNotFoundException)
          {
            break;
          }
          catch (Exception ex)
          {
            Console.WriteLine(
              "{0} when attempting to delete directory:\n{1}",
              ex.GetType().Name,
              ex.Message);

            Thread.Sleep(3000);
          }
        }
      }

      var dirArchiveFork = new DirectoryInfo(PathBlockArchiveFork);

      string filename = Path.GetFileName(FileBlockArchive.Name);
      FileBlockArchive.Dispose();
      
      foreach (FileInfo archiveFork in dirArchiveFork.GetFiles())
      {
        archiveFork.MoveTo(PathBlockArchive);
      }

      OpenBlockArchive();

      Directory.Delete(PathBlockArchiveFork);
      DismissFork();
    }
        

    bool IsInserterCompleted;
    Dictionary<int, BlockLoad> QueueBlockArchives =
      new Dictionary<int, BlockLoad>();
    readonly object LOCK_QueueBlockArchives = new object();

    async Task StartLoader()
    {
      var parser = new UTXOTable.BlockParser();

    LABEL_LoadBlockArchive:

      var blockArchiveLoad = new BlockLoad();

      lock (LOCK_IndexBlockArchiveLoad)
      {
        blockArchiveLoad.Index = IndexBlockArchiveLoad;
        IndexBlockArchiveLoad += 1;
      }

      string pathFile = Path.Combine(
        PathBlockArchive,
        blockArchiveLoad.Index.ToString());

      try
      {
        byte[] bytesFile = File.ReadAllBytes(pathFile);
        int startIndex = 0;
        Block block;

        while (startIndex < bytesFile.Length)
        {
          block = parser.ParseBlock(
            bytesFile,
            ref startIndex);

          blockArchiveLoad.InsertBlock(block);
        }

        blockArchiveLoad.IsInvalid = false;
      }
      catch (Exception ex)
      {
        blockArchiveLoad.IsInvalid = true;

        string.Format(
          "Loader throws exception {0} \n" +
          "when parsing file {1}",
          pathFile,
          ex.Message)
          .Log(LogFile);
      }

      while (true)
      {
        if (IsInserterCompleted)
        {
          return;
        }

        if (QueueLoader.Count < COUNT_LOADER_TASKS)
        {
          lock (LOCK_QueueBlockArchives)
          {
            if (blockArchiveLoad.Index == IndexBlockArchiveQueue)
            {
              break;
            }

            if (QueueBlockArchives.Count <= COUNT_LOADER_TASKS)
            {
              QueueBlockArchives.Add(
                blockArchiveLoad.Index,
                blockArchiveLoad);

              if (blockArchiveLoad.IsInvalid)
              {
                return;
              }

              goto LABEL_LoadBlockArchive;
            }
          }
        }

        await Task.Delay(2000).ConfigureAwait(false);
      }

      while (true)
      {
        QueueLoader.Post(blockArchiveLoad);

        if (blockArchiveLoad.IsInvalid)
        {
          return;
        }

        lock (LOCK_QueueBlockArchives)
        {
          IndexBlockArchiveQueue += 1;

          if (QueueBlockArchives.TryGetValue(
            IndexBlockArchiveQueue,
            out blockArchiveLoad))
          {
            QueueBlockArchives.Remove(
              blockArchiveLoad.Index);
          }
          else
          {
            goto LABEL_LoadBlockArchive;
          }
        }
      }
    }

  }
}
