using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Security.Cryptography;



namespace BToken.Chaining
{
  public partial class Headerchain : DataArchiver.IDataStructure
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


    public HeaderBranch CreateBranch()
    {
      return new HeaderBranch(
        HeaderTip,
        AccumulatedDifficulty,
        Height);
    }

    public List<byte[]> GetLocator()
    {
      return Locator.BlockLocations
          .Select(b => b.Hash)
          .ToList();
    }
       
    public void CommitBranch(HeaderBranch headerBranch)
    {
      headerBranch.HeaderAncestor.HeaderNext = 
        headerBranch.HeaderRoot;

      HeaderTip = headerBranch.HeaderInsertedLast;
      
      AccumulatedDifficulty = 
        headerBranch.AccumulatedDifficultyInserted;

      Height = headerBranch.HeightInserted;
    }


    public bool ContainsHeader(
      byte[] headerHash)
    {
      return TryReadHeader(
        headerHash,
        out Header header);
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