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



    public async Task<HeaderBranch> CreateHeaderBranch(
      BlockchainPeer peer)
    {
      List<byte[]> headerLocator = Locator.BlockLocations
        .Select(b => b.Hash).ToList();

      var headerContainer = await peer.GetHeaders(headerLocator);

      if (headerContainer.CountItems == 0)
      {
        return null;
      }

      int indexLocatorRoot = headerLocator.FindIndex(
        h => h.IsEqual(
          headerContainer.HeaderRoot.HashPrevious));

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

      Header headerBranchRoot = headerContainer.HeaderRoot;

      while (Contains(headerBranchRoot.Hash))
      {
        if (stopHash.IsEqual(headerBranchRoot.Hash))
        {
          throw new ChainException(
            "Error in headerchain synchronization.");
        }

        if (headerBranchRoot.HeaderNext == null)
        {
          headerLocator = new List<byte[]>
              { headerBranchRoot.Hash };

          headerContainer = await peer.GetHeaders(headerLocator);

          if (headerContainer.CountItems == 0)
          {
            throw new ChainException(
              "Error in headerchain synchronization.");
          }

          if (!headerContainer.HeaderRoot.HashPrevious
            .IsEqual(headerBranchRoot.Hash))
          {
            throw new ChainException(
              "Error in headerchain synchronization.");
          }

          headerBranchRoot = headerContainer.HeaderRoot;
        }
        else
        {
          headerBranchRoot = headerBranchRoot.HeaderNext;
        }
      }

      if (!Contains(headerBranchRoot.HashPrevious))
      {
        throw new ChainException(
          "Error in headerchain synchronization.");
      }

      Header headerBranchTip = headerContainer.HeaderTip;

      while (true)
      {
        headerLocator = new List<byte[]> {
                headerBranchTip.Hash };

        headerContainer = await peer.GetHeaders(headerLocator);

        if (headerContainer.CountItems == 0)
        {
          break;
        }

        if (!headerContainer.HeaderRoot.HashPrevious
          .IsEqual(headerBranchTip.Hash))
        {
          throw new ChainException(
            "Error in headerchain synchronization.");
        }

        headerBranchTip.HeaderNext = headerContainer.HeaderRoot;
        headerContainer.HeaderRoot.HeaderPrevious = headerBranchTip;
        headerBranchTip = headerContainer.HeaderTip;
      }

      return new HeaderBranch(
        headerBranchRoot,
        headerBranchTip);
    }



    Header HeaderBranchAncestor;

    public void StageBranch(HeaderBranch headerBranch)
    {
      Header headerTipStaged = headerBranch.HeaderRoot;

      HeaderBranchAncestor = HeaderTip;
      double accumulatedDifficultyStaged = AccumulatedDifficulty;
      int heightStaged = Height;

      while (!HeaderBranchAncestor.Hash.IsEqual(
        headerBranch.HeaderRoot.HashPrevious))
      {
        headerBranch.IsFork = true;

        accumulatedDifficultyStaged -= TargetManager.GetDifficulty(
          HeaderBranchAncestor.NBits);

        heightStaged--;

        HeaderBranchAncestor = HeaderBranchAncestor.HeaderPrevious;
      }

      headerBranch.HeaderRoot.HeaderPrevious = HeaderBranchAncestor;
      headerBranch.AccumulatedDifficultyInserted = accumulatedDifficultyStaged;

      while (true)
      {
        heightStaged += 1;

        double difficulty = TargetManager.GetDifficulty(
          headerTipStaged.NBits);

        headerBranch.HeaderDifficulties.Add(difficulty);

        accumulatedDifficultyStaged += difficulty;

        if (headerBranch.HeaderForkTip == null &&
          accumulatedDifficultyStaged > AccumulatedDifficulty)
        {
          headerBranch.HeaderForkTip = headerTipStaged;
        }

        uint medianTimePast = GetMedianTimePast(
          headerTipStaged.HeaderPrevious);
        if (headerTipStaged.UnixTimeSeconds < medianTimePast)
        {
          throw new ChainException(
            string.Format(
              "Header {0} with unix time {1} " +
              "is older than median time past {2}.",
              headerTipStaged.Hash.ToHexString(),
              DateTimeOffset.FromUnixTimeSeconds(headerTipStaged.UnixTimeSeconds),
              DateTimeOffset.FromUnixTimeSeconds(medianTimePast)),
            ErrorCode.INVALID);
        }

        int hightHighestCheckpoint = Checkpoints.Max(x => x.Height);

        if (
          hightHighestCheckpoint <= heightStaged &&
          heightStaged <= hightHighestCheckpoint)
        {
          throw new ChainException(
            string.Format(
              "Attempt to insert header {0} at hight {1} " +
              "prior to checkpoint hight {2}",
              headerTipStaged.Hash.ToHexString(),
              Height,
              hightHighestCheckpoint),
            ErrorCode.INVALID);
        }

        HeaderLocation checkpoint =
          Checkpoints.Find(c => c.Height == heightStaged);
        if (
          checkpoint != null && 
          !checkpoint.Hash.IsEqual(headerTipStaged.Hash))
        {
          throw new ChainException(
            string.Format(
              "Header {0} at hight {1} not equal to checkpoint hash {2}",
              headerTipStaged.Hash.ToHexString(),
              Height,
              checkpoint.Hash.ToHexString()),
            ErrorCode.INVALID);
        }

        uint targetBits = TargetManager.GetNextTargetBits(
            headerTipStaged.HeaderPrevious,
            (uint)heightStaged);

        if (headerTipStaged.NBits != targetBits)
        {
          throw new ChainException(
            string.Format(
              "In header {0} nBits {1} not equal to target nBits {2}",
              headerTipStaged.Hash.ToHexString(),
              headerTipStaged.NBits,
              targetBits),
            ErrorCode.INVALID);
        }
        
        if(headerTipStaged.HeaderNext != null)
        {
          headerTipStaged = headerTipStaged.HeaderNext;
        }
        else
        {
          if (accumulatedDifficultyStaged > AccumulatedDifficulty)
          {
            return;
          }

          throw new ChainException(
            string.Format(
              "staged header branch {0} with hight {1} not " +
              "stronger than main branch {2} with height {3}",
              headerTipStaged.Hash.ToHexString(),
              heightStaged,
              HeaderTip.Hash.ToHexString(),
              Height),
            ErrorCode.INVALID);
        }
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
    
    public void CommitBranch(HeaderBranch headerBranch)
    {
      HeaderBranchAncestor.HeaderNext = headerBranch.HeaderRoot;

      HeaderTip = headerBranch.HeaderLastInserted;
      
      AccumulatedDifficulty = 
        headerBranch.AccumulatedDifficultyInserted;

      Height = headerBranch.HeightInserted;
    }


    public bool Contains(byte[] headerHash)
    {
      SHA256 sHA256 = SHA256.Create();

      int key = BitConverter.ToInt32(headerHash, 0);

      if (HeaderIndex.TryGetValue(key, out List<Header> headers))
      {
        return headers.Any(h => headerHash.IsEqual(h.Hash));
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
        if(TryReadHeader(hash, out Header header))
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