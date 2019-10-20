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

    HeaderchainSynchronizer Synchronizer;


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

      Synchronizer = new HeaderchainSynchronizer(
        network,
        this);
    }



    public async Task Start()
    {
      await Synchronizer.Start();
    }



    public DataBatchContainer LoadDataContainer(int batchIndex)
    {
      var container = new HeaderBatchContainer(batchIndex);

      try
      {
        container.Buffer = File.ReadAllBytes(FilePath + batchIndex);
      }
      catch (IOException)
      {
        container.IsValid = false;
      }

      return container;
    }



    const int SIZE_OUTPUT_BATCH = 50000;
    int CountItems;
    int ArchiveIndex;
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
        
        bool isFinalContainer = batch.IsFinalBatch &&
          (container == batch.ItemBatchContainers.Last());

        if (CountItems >= SIZE_OUTPUT_BATCH || isFinalContainer)
        {
          ArchiveContainers(Containers);

          if (CountItems >= SIZE_OUTPUT_BATCH)
          {
            Containers = new List<DataBatchContainer>();
            CountItems = 0;

            ArchiveIndex += 1;
          }
        }
      }

      Console.WriteLine("Blockheight {0}," +
        "Inserted batch {1} with {2} headers in headerchain", 
        GetHeight(),
        batch.Index,
        batch.CountItems);

      return true;
    }
        


    public bool TryInsertHeaderBytes(
      byte[] buffer,
      out List<Header> headers)
    {
      headers = new List<Header>();

      HeaderBatchContainer container =
        new HeaderBatchContainer(
          ArchiveIndex,
          buffer);

      container.Parse();

      if (
        !container.IsValid || 
        !TryInsertContainer(container))
      {
        return false;
      }

      ArchiveContainers(Containers);

      if (CountItems >= SIZE_OUTPUT_BATCH)
      {
        Containers = new List<DataBatchContainer>();
        CountItems = 0;

        ArchiveIndex += 1;
      }

      Header header = container.HeaderRoot;
      headers.Add(header);
      while(header != container.HeaderTip)
      {
        header = header.HeadersNext[0];
        headers.Add(header);
      }

      return true;
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
          "Insertion of header container {0} raised ChainException:\n {1}",
          container.Index,
          ex.Message);

        return false;
      }

      if (rivalChain != null 
        && rivalChain.IsStrongerThan(MainChain))
      {
        ReorganizeChain(rivalChain);
      }
      
      Containers.Add(container);
      CountItems += container.CountItems;

      return true;
    }



    void ReorganizeChain(Chain chain)
    {
      SecondaryChains.Remove(chain);
      SecondaryChains.Add(MainChain);
      MainChain = chain;

      Locator.Reorganize();
    }



    void LoadImage(out int batchIndexMergedLast)
    {
      batchIndexMergedLast = 0;
      return;
    }



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
  }
}
