using System;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using System.Threading.Tasks;
using System.Security.Cryptography;

using BToken.Networking;


namespace BToken.Chaining
{
  partial class Headerchain : IDatabase
  {
    Chain MainChain;
    List<Chain> SecondaryChains = new List<Chain>();
    public Header GenesisHeader;
    List<HeaderLocation> Checkpoints;

    readonly object HeaderIndexLOCK = new object();
    Dictionary<int, List<Header>> HeaderIndex;

    public HeaderLocator Locator;

    ChainInserter Inserter;

    const int HEADERS_COUNT_MAX = 2000;

    static string ArchiveRootPath = Path.Combine(
      AppDomain.CurrentDomain.BaseDirectory,
      "HeaderArchive");

    static DirectoryInfo RootDirectory =
      Directory.CreateDirectory(ArchiveRootPath);

    string FilePath = Path.Combine(RootDirectory.Name, "h");

    public readonly object LOCK_Chain = new object();

    GatewayHeaderchain Gateway;


    public Headerchain(
      Header genesisHeader,
      List<HeaderLocation> checkpoints,
      Network network)
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

      Gateway = new GatewayHeaderchain(
        network,
        this);
    }



    public async Task Start()
    {
      await Gateway.Start();
    }


    public void LoadImage(out int batchIndexMergedLast)
    {
      batchIndexMergedLast = 1;
      return;
    }



    byte[] GetHeaderHashPrevious(DataBatch dataBatch)
    {
      HeaderBatchContainer firstContainer =
        (HeaderBatchContainer)dataBatch.ItemBatchContainers.First();

      return firstContainer.HeaderRoot.HeaderHash;
    }

    public bool TryInsertBatch(DataBatch batch, out ItemBatchContainer containerInvalid)
    {
      Chain rivalChain;

      foreach (HeaderBatchContainer headerContainer in batch.ItemBatchContainers)
      {
        try
        {
          rivalChain = Inserter.InsertChain(headerContainer.HeaderRoot);
        }
        catch (ChainException ex)
        {
          Console.WriteLine(
            "Insertion of batch {0} raised ChainException:\n {1}.",
            batch.Index,
            ex.Message);

          containerInvalid = headerContainer;
          return false;
        }

        if (rivalChain != null && rivalChain.IsStrongerThan(MainChain))
        {
          ReorganizeChain(rivalChain);
        }
      }

      Console.WriteLine("Inserted batch {0} in headerchain", batch.Index);

      containerInvalid = null;
      return true;
    }

    void ReorganizeChain(Chain chain)
    {
      SecondaryChains.Remove(chain);
      SecondaryChains.Add(MainChain);
      MainChain = chain;

      Locator.Reorganize();
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

    public ItemBatchContainer LoadDataContainer(int batchIndex)
    {
      return new HeaderBatchContainer(
        batchIndex,
        File.ReadAllBytes(FilePath + batchIndex));
    }

    public bool TryInsertContainer(ItemBatchContainer container)
    {
      HeaderBatchContainer headerContainer = (HeaderBatchContainer)container;
      Chain rivalChain;

      try
      {
        rivalChain = Inserter.InsertChain(headerContainer.HeaderRoot);
      }
      catch (ChainException ex)
      {
        Console.WriteLine(
          "Insertion of header container {0} raised ChainException:\n {1}.",
          headerContainer.Index,
          ex.Message);

        return false;
      }

      if (rivalChain != null && rivalChain.IsStrongerThan(MainChain))
      {
        ReorganizeChain(rivalChain);
      }

      Console.WriteLine("Inserted header container {0}", headerContainer.Index);

      return true;
    }

    public async Task ArchiveBatch(DataBatch batch)
    {
      using (FileStream fileStream = new FileStream(
        FilePath + batch.Index,
        FileMode.Create,
        FileAccess.Write,
        FileShare.None,
        bufferSize: 65536,
        useAsync: true))
      {
        foreach (HeaderBatchContainer batchContainer in batch.ItemBatchContainers)
        {
          await fileStream.WriteAsync(
            batchContainer.Buffer,
            0,
            batchContainer.Buffer.Length);
        }
      }
    }

    public int GetHeight()
    {
      return MainChain.Height;
    }
  }
}
