using System;
using System.Collections.Generic;
using System.Linq;
using System.IO;
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


    void LoadImage(out int batchIndexMergedLast)
    {
      batchIndexMergedLast = 0;
      return;
    }



    byte[] GetHeaderHashPrevious(DataBatch dataBatch)
    {
      HeaderBatchContainer firstContainer =
        (HeaderBatchContainer)dataBatch.ItemBatchContainers.First();

      return firstContainer.HeaderRoot.HeaderHash;
    }



    const int SIZE_OUTPUT_BATCH = 50000;
    int CountItems;

    List<DataBatchContainer> Containers = new List<DataBatchContainer>();

    bool TryInsertBatch(DataBatch batch)
    {
      foreach (HeaderBatchContainer container 
        in batch.ItemBatchContainers)
      {
        if(!TryInsertContainer(container))
        {
          return false;
        }

        Containers.Add(container);
        CountItems += container.CountItems;

        bool isFinalContainer = batch.IsFinalBatch &&
          (container == batch.ItemBatchContainers.Last());

        if (CountItems > SIZE_OUTPUT_BATCH || isFinalContainer)
        {
          ArchiveContainers(Containers);

          Containers = new List<DataBatchContainer>();
          CountItems = 0;

          ArchiveIndex += 1;
        }
      }

      Console.WriteLine("Blockheight {0}," +
        "Inserted batch {1} with {2} headers in headerchain", 
        GetHeight(),
        batch.Index,
        batch.CountItems);

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

    public DataBatchContainer LoadDataContainer(int batchIndex)
    {
      return new HeaderBatchContainer(
        batchIndex,
        File.ReadAllBytes(FilePath + batchIndex));
    }


    bool TryInsertContainer(HeaderBatchContainer container)
    {
      Chain rivalChain;

      try
      {
        rivalChain = Inserter.InsertChain(container.HeaderRoot);
      }
      catch (ChainException ex)
      {
        Console.WriteLine(
          "Insertion of header container {0} raised ChainException:\n {1}.",
          container.Index,
          ex.Message);

        return false;
      }

      if (rivalChain != null 
        && rivalChain.IsStrongerThan(MainChain))
      {
        ReorganizeChain(rivalChain);
      }
      
      return true;
    }

    int ArchiveIndex;
    string ArchivePath = RootDirectory.Name;

    async Task ArchiveContainers(List<DataBatchContainer> containers)
    {
      string filePath =
        Path.Combine(ArchivePath, "h" + ArchiveIndex);

      try
      {
        using (FileStream fileStream = new FileStream(
          filePath,
          FileMode.Create,
          FileAccess.Write,
          FileShare.None,
          bufferSize: 65536,
          useAsync: true))
        {
          foreach (HeaderBatchContainer container in containers)
          {
            await fileStream.WriteAsync(
              container.Buffer,
              0,
              container.Buffer.Length);
          }
        }
      }
      catch (Exception ex)
      {
        Console.WriteLine(ex.Message);
      }
    }

    public int GetHeight()
    {
      return MainChain.Height;
    }
  }
}
