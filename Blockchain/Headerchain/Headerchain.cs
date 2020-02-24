using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Security.Cryptography;



namespace BToken.Blockchain
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


    Header HeaderBranchRoot;
    Header HeaderBranchStaged;
    Header HeaderTipStaged;
    int HeightStaged;
    double AccumulatedDifficultyStaged;

    public Header StageFork(ref Header headerBranch)
    {
      HeaderBranchStaged = headerBranch;
      HeaderTipStaged = headerBranch;

      HeaderBranchRoot = HeaderTip;
      AccumulatedDifficultyStaged = AccumulatedDifficulty;
      HeightStaged = Height;

      while (!HeaderBranchRoot.HeaderHash.IsEqual(
        HeaderBranchStaged.HashPrevious))
      {
        AccumulatedDifficultyStaged -= TargetManager.GetDifficulty(
          HeaderBranchRoot.NBits);

        HeightStaged--;

        HeaderBranchRoot = HeaderBranchRoot.HeaderPrevious;
      }
      
      HeaderBranchStaged.HeaderPrevious = HeaderBranchRoot;

      while (true)
      {
        HeightStaged += 1;
        AccumulatedDifficultyStaged += TargetManager.GetDifficulty(
          HeaderTipStaged.NBits);

        uint medianTimePast = GetMedianTimePast(
          HeaderTipStaged.HeaderPrevious);
        if (HeaderTipStaged.UnixTimeSeconds < medianTimePast)
        {
          throw new ChainException(
            string.Format(
              "Header {0} with unix time {1} " +
              "is older than median time past {2}.",
              HeaderTipStaged.HeaderHash.ToHexString(),
              DateTimeOffset.FromUnixTimeSeconds(HeaderTipStaged.UnixTimeSeconds),
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
              HeaderTipStaged.HeaderHash.ToHexString(),
              Height,
              hightHighestCheckpoint),
            ErrorCode.INVALID);
        }

        HeaderLocation checkpoint =
          Checkpoints.Find(c => c.Height == HeightStaged);
        if (
          checkpoint != null && 
          !checkpoint.Hash.IsEqual(HeaderTipStaged.HeaderHash))
        {
          throw new ChainException(
            string.Format(
              "Header {0} at hight {1} not equal to checkpoint hash {2}",
              HeaderTipStaged.HeaderHash.ToHexString(),
              Height,
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
        
        if (AccumulatedDifficultyStaged > AccumulatedDifficulty)
        {
          headerBranch = HeaderTipStaged.HeaderNext;
          HeaderTipStaged.HeaderNext = null;
          return HeaderBranchStaged;
        }

        if(HeaderTipStaged.HeaderNext != null)
        {
          HeaderTipStaged = HeaderTipStaged.HeaderNext;
        }
        else
        {
          throw new ChainException(
            string.Format(
              "staged header branch {0} with hight {1} not " +
              "stronger than main branch {2} with height {3}",
              HeaderTipStaged.HeaderHash.ToHexString(),
              HeightStaged,
              HeaderTip.HeaderHash.ToHexString(),
              Height),
            ErrorCode.INVALID);
        }
      }
    }
        
    public bool IsForkStaged()
    {
      return HeaderBranchStaged != null;
    }

    public void UnstageFork()
    {
      HeaderBranchStaged = null;
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
    
    public bool IsHeaderCommitFork(Header header)
    {
      return HeaderTipStaged == header;
    }

    public void CommitFork()
    {
      HeaderBranchRoot.HeaderNext = HeaderBranchStaged;
      HeaderTip = HeaderTipStaged;
      AccumulatedDifficulty = AccumulatedDifficultyStaged;
      Height = HeightStaged;

      HeaderBranchStaged = null;
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



    public async Task<Header> CreateHeaderBranch(
      BlockchainPeer peer)
    {
      Header headerBranch = null;
      List<byte[]> headerLocator = GetLocator();

      var headerContainer = await peer.GetHeaders(headerLocator);

      if (headerContainer.CountItems == 0)
      {
        return headerBranch;
      }

      headerBranch = headerContainer.HeaderRoot;

      int indexLocatorRoot = headerLocator.FindIndex(
        h => h.IsEqual(headerBranch.HashPrevious));

      if (indexLocatorRoot == -1)
      {
        throw new ChainException(
          "Error in headerchain synchronization.");
      }

      byte[] stopHash;
      if (indexLocatorRoot == headerLocator.Count - 1)
      {
        stopHash =
         ("00000000000000000000000000000000" +
         "00000000000000000000000000000000").ToBinary();
      }
      else
      {
        stopHash = headerLocator[indexLocatorRoot + 1];
      }

      while (Contains(headerBranch.HeaderHash))
      {
        if (stopHash.IsEqual(headerBranch.HeaderHash))
        {
          throw new ChainException(
            "Error in headerchain synchronization.");
        }

        if (headerBranch.HeaderNext == null)
        {
          headerLocator = new List<byte[]>
              { headerBranch.HeaderHash };

          headerContainer = await peer.GetHeaders(headerLocator);

          if (headerContainer.CountItems == 0)
          {
            throw new ChainException(
              "Error in headerchain synchronization.");
          }

          if (!headerContainer.HeaderRoot.HashPrevious
            .IsEqual(headerBranch.HeaderHash))
          {
            throw new ChainException(
              "Error in headerchain synchronization.");
          }

          headerBranch = headerContainer.HeaderRoot;
        }
        else
        {
          headerBranch = headerBranch.HeaderNext;
        }
      }

      if (!Contains(headerBranch.HashPrevious))
      {
        throw new ChainException(
          "Error in headerchain synchronization.");
      }

      Header headerBranchTip = headerContainer.HeaderTip;

      while (true)
      {
        headerLocator = new List<byte[]> {
                headerBranchTip.HeaderHash };

        headerContainer = await peer.GetHeaders(headerLocator);

        if (headerContainer.CountItems == 0)
        {
          break;
        }

        if (!headerContainer.HeaderRoot.HashPrevious
          .IsEqual(headerBranchTip.HeaderHash))
        {
          throw new ChainException(
            "Error in headerchain synchronization.");
        }

        headerBranchTip.HeaderNext = headerContainer.HeaderRoot;
        headerContainer.HeaderRoot.HeaderPrevious = headerBranchTip;
        headerBranchTip = headerContainer.HeaderTip;
      }

      return headerBranch;
    }
  }
}