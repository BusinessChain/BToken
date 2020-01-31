using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Security.Cryptography;



namespace BToken.Chaining
{
  partial class Headerchain : DataArchiver.IDataStructure
  {
    public Chain MainChain;
    public Header GenesisHeader;
    List<HeaderLocation> Checkpoints;

    readonly object HeaderIndexLOCK = new object();
    Dictionary<int, List<Header>> HeaderIndex;

    public HeaderLocator Locator;

    ChainInserter Inserter;

    public readonly object LOCK_IsChainLocked = new object();
    bool IsChainLocked;

    public HeaderchainSynchronizer Synchronizer;

    public Header HeaderTip;
    public int Height;
    public double AccumulatedDifficulty;


    public Headerchain(
      Header genesisHeader,
      List<HeaderLocation> checkpoints)
    {
      GenesisHeader = genesisHeader;
      Checkpoints = checkpoints;

      MainChain = new Chain(
        GenesisHeader,
        0,
        TargetManager.GetDifficulty(GenesisHeader.NBits));

      HeaderIndex = new Dictionary<int, List<Header>>();
      UpdateHeaderIndex(GenesisHeader);

      Locator = new HeaderLocator(this);
      Inserter = new ChainInserter(this);

      Synchronizer = new HeaderchainSynchronizer(this);
    }



    public async Task Start()
    {
      await Synchronizer.Start();

      Console.WriteLine("Headerchain height {0}", 
        MainChain.Height);
    }


    Header HeaderBranchRootStaged;
    Header HeaderBranchStaged;
    Header HeaderTipStaged;
    Header HeaderNewTipStaged;
    int HeightStaged;
    double AccumulatedDifficultyStaged;

    public void StageHeaderBranch(
      Header headerBranch, 
      out int heightHeaderBranchRootStaged)
    {
      HeaderBranchStaged = headerBranch;
      HeaderTipStaged = headerBranch;
      HeaderBranchRootStaged = HeaderTip;
      AccumulatedDifficultyStaged = AccumulatedDifficulty;
      HeightStaged = Height;

      while (!HeaderBranchRootStaged.HeaderHash.IsEqual(
        HeaderBranchStaged.HashPrevious))
      {
        AccumulatedDifficultyStaged -= TargetManager.GetDifficulty(
          HeaderBranchRootStaged.NBits);

        HeightStaged--;

        HeaderBranchRootStaged = HeaderBranchRootStaged.HeaderPrevious;
      }

      heightHeaderBranchRootStaged = HeightStaged;
      HeaderBranchStaged.HeaderPrevious = HeaderBranchRootStaged;

      while (true)
      {
        HeightStaged += 1;
        AccumulatedDifficultyStaged += TargetManager.GetDifficulty(HeaderTipStaged.NBits);

        uint medianTimePast = GetMedianTimePast(HeaderTipStaged.HeaderPrevious);
        if (HeaderTipStaged.UnixTimeSeconds < medianTimePast)
        {
          throw new ChainException(
            string.Format(
              "Header {0} with unix time {1} " +
              "is older than median time past {2}.",
              headerTipStaged.HeaderHash.ToHexString(),
              DateTimeOffset.FromUnixTimeSeconds(headerTipStaged.UnixTimeSeconds),
              DateTimeOffset.FromUnixTimeSeconds(medianTimePast)),
            ErrorCode.INVALID);
        }

        int hightHighestCheckpoint = Checkpoints.Max(x => x.Height);

        if (
          hightHighestCheckpoint <= HeightStaged &&
          HeightStaged <= hightHighestCheckpoint)
        {
          throw new ChainException(
            string.Format(
              "Attempt to insert header {0} at hight {1} " +
              "prior to checkpoint hight {2}",
              headerTipStaged.HeaderHash.ToHexString(),
              height,
              hightHighestCheckpoint),
            ErrorCode.INVALID);
        }

        HeaderLocation checkpoint =
          Checkpoints.Find(c => c.Height == HeightStaged);
        if (checkpoint != null && !checkpoint.Hash.IsEqual(HeaderTipStaged.HeaderHash))
        {
          throw new ChainException(
            string.Format(
              "Header {0} at hight {1} not equal to checkpoint hash {2}",
              headerTipStaged.HeaderHash.ToHexString(),
              height,
              checkpoint.Hash.ToHexString()),
            ErrorCode.INVALID);
        }

        uint targetBits = TargetManager.GetNextTargetBits(
            HeaderTipStaged.HeaderPrevious,
            (uint)HeightStaged);

        if (HeaderTipStaged.NBits != targetBits)
        {
          throw new ChainException(
            string.Format(
              "In header {0} nBits {1} not equal to target nBits {2}",
              HeaderTipStaged.HeaderHash.ToHexString(),
              HeaderTipStaged.NBits,
              targetBits),
            ErrorCode.INVALID);
        }
        
        if (HeaderTipStaged.HeaderNext == null)
        {
          break;
        }

        HeaderTipStaged = HeaderTipStaged.HeaderNext;
      }
      
      if (AccumulatedDifficultyStaged <= AccumulatedDifficulty)
      {
        throw new ChainException(
          string.Format(
            "staged header branch {0} with hight {1} not " +
            "stronger than main branch {2} with height {3}",
            headerTipStaged.HeaderHash.ToHexString(),
            HeightStaged,
            HeaderTip.HeaderHash.ToHexString(),
            Height),
          ErrorCode.INVALID);
      }
    }
        
    uint GetMedianTimePast(Header header)
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

    public void CommitNewTip()
    {
      HeaderBranchRootStaged.HeaderNext = HeaderBranchStaged;
      HeaderTip = HeaderNewTipStaged;
      AccumulatedDifficulty = AccumulatedDifficultyStaged;
      Height = HeightStaged;
    }

    public void CommitNextBlock()
    {

    }

    // Ich mach staging nur für den Teil bis newTip, danach wird der 
    // tip blockweise hinaufgeschoben.
    public void Commit()
    {
      HeaderBranchRootStaged.HeaderNext = HeaderBranchStaged;
      HeaderTip = HeaderTipStaged;
      AccumulatedDifficulty = AccumulatedDifficultyStaged;
      Height = HeightStaged;
    }



    public void InsertContainer(HeaderContainer container)
    {
      Chain rivalChain = Inserter.InsertHeaderRoot(
        container.HeaderRoot);

      if (
        rivalChain != null
        && rivalChain.IsStrongerThan(MainChain))
      {
        SecondaryChains.Remove(rivalChain);
        SecondaryChains.Add(MainChain);
        MainChain = rivalChain;

        Locator.Reorganize();
      }

      Console.WriteLine(
        "Inserted {0} headers, tip {1}",
        container.CountItems,
        MainChain.HeaderTip.HeaderHash.ToHexString());
    }



    public bool Contains(byte[] headerHash)
    {
      SHA256 sHA256 = SHA256.Create();

      int key = BitConverter.ToInt32(headerHash, 0);

      if (HeaderIndex.TryGetValue(key, out List<Header> headers))
      {
        return headers.Any(h => headerHash.IsEqual(h.HeaderHash));
      }

      return false;
    }

    public bool TryReadHeader(
      byte[] headerHash,
      out Header header)
    {
      SHA256 sHA256 = SHA256.Create();

      return TryReadHeader(
        headerHash, 
        sHA256, 
        out header);
    }

    public bool TryReadHeader(
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
            if (headerHash.IsEqual(h.HeaderHash))
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
      int keyHeader = BitConverter.ToInt32(header.HeaderHash, 0);

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
        if(TryReadHeader(hash, out Header header))
        {
          List<Header> headers = new List<Header>();

          while (
            header.HeaderNext != null &&
            headers.Count < count &&
            !header.HeaderHash.IsEqual(stopHash))
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


    public List<byte[]> GetLocator()
    {
      return Locator.BlockLocations
        .Select(b => b.Hash).ToList();
    }
  }
}