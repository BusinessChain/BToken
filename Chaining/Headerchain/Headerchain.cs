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
    static List<HeaderLocation> Checkpoints;

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


    
    public async Task<HeaderBranch> StageBranch(BlockchainPeer peer)
    {
      List<byte[]> locator = Locator.BlockLocations
          .Select(b => b.Hash)
          .ToList();

      HeaderContainer headerContainer = await peer.GetHeaders(locator);

      if(headerContainer.HeaderRoot == null)
      {
        return null;
      }

      var headerBranch = new HeaderBranch(
        HeaderTip, 
        AccumulatedDifficulty,
        Height);

      HeaderContainer headerContainerNext;

      do
      {
        headerBranch.AddContainer(headerContainer);

        headerContainerNext = await peer.GetHeaders(locator);
        
        if (!headerContainerNext.HeaderRoot.HeaderPrevious.Hash
          .IsEqual(headerContainer.HeaderTip.Hash))
        {
          throw new ChainException("Received headers do not chain.");
        }

        headerContainer = headerContainerNext;

      } while (headerContainerNext.HeaderRoot != null);

      if (headerBranch.AccumulatedDifficulty > AccumulatedDifficulty)
      {
        return headerBranch;
      }

      throw new ChainException(
        string.Format(
          "staged header branch {0} with hight {1} not " +
          "stronger than main branch {2} with height {3}",
          headerBranch.HeaderTip.Hash.ToHexString(),
          headerBranch.Height,
          HeaderTip.Hash.ToHexString(),
          Height),
        ErrorCode.INVALID);
    }
    
        
    
    public void CommitBranch(HeaderBranch headerBranch)
    {
      headerBranch.HeaderAncestor.HeaderNext = 
        headerBranch.HeaderRoot;

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