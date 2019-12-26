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


    async Task LockChain()
    {
      while(true)
      {
        lock(LOCK_IsChainLocked)
        {
          if(!IsChainLocked)
          {
            IsChainLocked = true;
            return;
          }
        }

        await Task.Delay(100);
      }
    }

    void ReleaseChain()
    {
      lock (LOCK_IsChainLocked)
      {
        if (IsChainLocked)
        {
          IsChainLocked = false;
        }
      }
    }



    Header HeaderBranchMain;
    public Header HeaderRootTentativeFork;
    public int HeightHeaderRootTentativeFork;

    public void InsertHeaderBranchTentative(
      Header header)
    {
      Inserter.InsertHeaderBranchTentative(header);
    }
    public bool IsBranchTentativeStrongerThanMain()
    {
      return Inserter.AccumulatedDifficulty > 
        MainChain.AccumulatedDifficulty;
    }
    public void ReorgTentativeToMainChain()
    {
      HeaderTip = Inserter.Header;
      Height = Inserter.Height;
      AccumulatedDifficulty = Inserter.AccumulatedDifficulty;

      Locator.Reorganize();
    }
    public void DismissTentativeChain()
    {
      HeaderRootTentativeFork.HeadersNext.Remove(
        HeaderRootTentativeFork.HeadersNext.Last());
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
  }
}