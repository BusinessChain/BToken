using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Security.Cryptography;

using BToken.Networking;



namespace BToken.Chaining
{
  partial class Headerchain
  {
    Chain MainChain;
    List<Chain> SecondaryChains = new List<Chain>();
    public Header GenesisHeader;
    List<HeaderLocation> Checkpoints;

    readonly object HeaderIndexLOCK = new object();
    Dictionary<int, List<Header>> HeaderIndex;

    public HeaderLocator Locator;

    ChainInserter Inserter;

    public readonly object LOCK_Chain = new object();

    public HeaderchainSynchronizer Synchronizer;
    public Network Network;


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
    }



    void InsertContainer(HeaderContainer container)
    {
      Chain rivalChain = Inserter.InsertHeaderRoot(
        container.HeaderRoot);

      Console.WriteLine("Inserted {0} header, blockheight {1}, tip {2}",
        container.CountItems,
        GetHeight(),
        container.HeaderTip.HeaderHash.ToHexString());

      if (
        rivalChain != null
        && rivalChain.IsStrongerThan(MainChain))
      {
        SecondaryChains.Remove(rivalChain);
        SecondaryChains.Add(MainChain);
        MainChain = rivalChain;

        Locator.Reorganize();
      }
    }



    public int GetHeight()
    {
      return MainChain.Height;
    }



    public Header ReadHeader(byte[] headerHash)
    {
      SHA256 sHA256 = SHA256.Create();

      return ReadHeader(headerHash, sHA256);
    }

    public Header ReadHeader(byte[] headerHash, SHA256 sHA256)
    {
      int key = BitConverter.ToInt32(headerHash, 0);

      lock (HeaderIndexLOCK)
      {
        if (HeaderIndex.TryGetValue(key, out List<Header> headers))
        {
          foreach (Header header in headers)
          {
            if (headerHash.IsEqual(header.HeaderHash))
            {
              return header;
            }
          }
        }
      }

      throw new ChainException(string.Format("Header hash {0} not in chain.",
        headerHash.ToHexString()));
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
      int count)
    {
      Header header = null;

      foreach (byte[] hash in locatorHashes)
      {
        try
        {
          header = ReadHeader(hash);
          break;
        }
        catch (ChainException)
        {
          continue;
        }
      }

      if (header == null)
      {
        throw new ChainException(string.Format(
          "Locator does not root in headerchain."));
      }

      List<Header> headers = new List<Header>();

      while (
        header.HeadersNext.Count > 0 &&
        headers.Count < count)
      {
        headers.Add(header.HeadersNext.First());
        header = header.HeadersNext.First();
      }

      return headers;
    }
  }
}