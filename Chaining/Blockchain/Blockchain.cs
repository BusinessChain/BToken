using System;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Security.Cryptography;



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
    Wallet Wallet;

    BlockchainNetwork Network;
    BlockArchiver Archiver;

    string FileNameIndexBlockArchiveImage = "IndexBlockArchive";
    DirectoryInfo DirectoryImage =
      Directory.CreateDirectory("image");
    DirectoryInfo DirectoryImageOld =
      Directory.CreateDirectory("image_old");

    readonly object LOCK_IsBlockchainLocked = new object();
    bool IsBlockchainLocked;

    int IndexBlockArchiveImage;

    long UTCTimeStartMerger = 
      DateTimeOffset.UtcNow.ToUnixTimeSeconds();



    public Blockchain(
      Header headerGenesis,
      byte[] genesisBlockBytes,
      Dictionary<int, byte[]> checkpoints)
    {
      Network = new BlockchainNetwork(this);
      Archiver = new BlockArchiver(this);

      HeaderGenesis = headerGenesis;
      HeaderTip = headerGenesis;

      Checkpoints = checkpoints;
            
      HeaderIndex = new Dictionary<int, List<Header>>();
      UpdateHeaderIndex(headerGenesis);
      
      UTXOTable = new UTXOTable(genesisBlockBytes);

      Wallet = new Wallet();
    }



    public async Task Start()
    {
      await LoadImage();

      Network.Start();

      while(true)
      {
        await Task.Delay(10000);

        Wallet.SendAnchorToken();
      }
    }

    async Task LoadImage()
    {
      await LoadImage(0, new byte[32]);
    }

    
    async Task LoadImage(
      int heightMax,
      byte[] stopHashInlcusive)
    {
      string pathImage = DirectoryImage.Name;

      while(true)
      {
        Initialize();

        if (!TryLoadImageFile(pathImage) ||
        (Height > heightMax && heightMax > 0))
        {
          if (pathImage == DirectoryImage.Name)
          {
            pathImage = DirectoryImageOld.Name;
            continue;
          }
        }
        
        if (await Archiver.TryLoadBlocks(
          stopHashInlcusive,
          IndexBlockArchiveImage))
        {
          return;
        }
      }
    }


    bool TryLoadImageFile(string pathImage)
    {
      try
      {
        LoadImageHeaderchain(pathImage);

        IndexBlockArchiveImage = BitConverter.ToInt32(
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

        IndexBlockArchiveImage = 0;
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
          throw new ChainException(
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

    void CreateImage(int indexBlockArchive)
    {
      try
      {
        while(true)
        {
          try
          {
            DirectoryImageOld.Delete(true);
            break;
          }
          catch(DirectoryNotFoundException)
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
              DirectoryImage.Name,
              DirectoryImageOld.Name);

            break;
          }
          catch(DirectoryNotFoundException)
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

        DirectoryImage.Create();

        string pathimageHeaderchain = Path.Combine(
          DirectoryImage.Name,
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
            DirectoryImage.Name,
            FileNameIndexBlockArchiveImage),
          BitConverter.GetBytes(indexBlockArchive));

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
      catch (Exception ex)
      {
        Console.WriteLine(
          "{0}:\n{1}",
          ex.GetType().Name,
          ex.Message);
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
        throw new ChainException(
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

    
    public void InsertBlock(
      Block block,
      bool flagValidateHeader)
    {
      block.Header.HeaderPrevious = HeaderTip;

      if (flagValidateHeader)
      {
        ValidateHeader(block.Header, Height + 1);
      }

      UTXOTable.InsertBlock(
        block,
        Archiver.IndexBlockArchive);
      
      InsertHeader(block.Header);
      
      Console.WriteLine(
        "{0},{1},{2},{3}",
        Height,
        Archiver.IndexBlockArchive,
        DateTimeOffset.UtcNow.ToUnixTimeSeconds() - UTCTimeStartMerger,
        UTXOTable.GetMetricsCSV());
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

      throw new ChainException(string.Format(
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



    readonly object LOCK_ParsersIdle = new object();
    Stack<UTXOTable.BlockParser> ParsersIdle =
      new Stack<UTXOTable.BlockParser>();
    
    UTXOTable.BlockParser GetBlockParser()
    {
      try
      {
        lock(LOCK_ParsersIdle)
        {
          var blockParser = ParsersIdle.Pop();

          blockParser.ClearPayloadData();
          return blockParser;
        }
      }
      catch (InvalidOperationException)
      {
        return new UTXOTable.BlockParser();
      }
    }

    void ReleaseBlockParser(
      UTXOTable.BlockParser parser)
    {
      parser.ClearPayloadData();

      lock (LOCK_ParsersIdle)
      {
        ParsersIdle.Push(parser);
      }
    }
  }
}
