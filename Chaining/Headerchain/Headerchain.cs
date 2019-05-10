using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Threading.Tasks.Dataflow;
using System.IO;
using System.Runtime.Remoting.Metadata.W3cXsd2001;
using System.Security.Cryptography;

using BToken.Networking;
using BToken.Hashing;


namespace BToken.Chaining
{
  public enum ChainCode { ORPHAN, DUPLICATE, INVALID };

  public partial class Headerchain
  {
    Chain MainChain;
    List<Chain> SecondaryChains = new List<Chain>();
    public ChainHeader GenesisHeader;
    List<HeaderLocation> Checkpoints;

    readonly object HeaderIndexLOCK = new object();
    Dictionary<int, List<ChainHeader>> HeaderIndex;

    public HeaderLocator Locator { get; private set; }

    BufferBlock<bool> SignalInserterAvailable = new BufferBlock<bool>();
    ChainInserter Inserter;

    const int HEADERS_COUNT_MAX = 2000;

    static string ArchiveRootPath = Path.Combine(
      AppDomain.CurrentDomain.BaseDirectory, 
      "HeaderArchive");

    static DirectoryInfo RootDirectory = Directory.CreateDirectory(ArchiveRootPath);
    static string FilePath = Path.Combine(RootDirectory.Name, "Headerchain");


    public Headerchain(
      NetworkHeader genesisHeader,
      List<HeaderLocation> checkpoints)
    {
      GenesisHeader = new ChainHeader(genesisHeader, null);
      Checkpoints = checkpoints;
      MainChain = new Chain(GenesisHeader, 0, 0);

      HeaderIndex = new Dictionary<int, List<ChainHeader>>();
      UpdateHeaderIndex(GenesisHeader, GenesisHeader.GetHeaderHash());

      Locator = new HeaderLocator(this);

      Inserter = new ChainInserter(this);
    }
    
    public async Task<List<byte[]>> InsertHeadersAsync(List<NetworkHeader> headers)
    {
      using (var archiveWriter = new HeaderWriter())
      {
        return await InsertHeadersAsync(archiveWriter, headers);
      }
    }
    public async Task<List<byte[]>> InsertHeadersAsync(HeaderWriter archiveWriter, List<NetworkHeader> headers)
    {
      var headersInserted = new List<byte[]>();

      foreach (NetworkHeader header in headers)
      {
        try
        {
          headersInserted.Add(await InsertHeaderAsync(header));
        }
        catch (ChainException ex)
        {
          Console.WriteLine(string.Format("Insertion of header with hash '{0}' raised ChainException '{1}'.",
            header.ComputeHash(), 
            ex.Message));

          return headersInserted;
        }

        archiveWriter.StoreHeader(header);
      }

      return headersInserted;
    }
    async Task<byte[]> InsertHeaderAsync(NetworkHeader header)
    {
      ValidateHeader(header, out byte[] headerHash);

      using (var inserter = await DispatchInserterAsync())
      {
        Chain rivalChain = inserter.InsertHeader(header, headerHash);

        if (rivalChain != null && rivalChain.IsStrongerThan(MainChain))
        {
          ReorganizeChain(rivalChain);
        }
      }

      return headerHash;
    }
    async Task<ChainInserter> DispatchInserterAsync()
    {
      await SignalInserterAvailable.ReceiveAsync();

      if (Inserter.TryDispatch())
      {
        return Inserter;
      }
      else
      {
        throw new ChainException("Received signal available but could not dispatch inserter.");
      }
    }
    static void ValidateHeader(NetworkHeader header, out byte[] headerHash)
    {
      headerHash = header.ComputeHash();

      if (headerHash.IsGreaterThan(header.NBits))
      {
        throw new ChainException(ChainCode.INVALID);
      }

      const long MAX_FUTURE_TIME_SECONDS = 2 * 60 * 60;
      bool IsTimestampPremature = header.UnixTimeSeconds > (DateTimeOffset.UtcNow.ToUnixTimeSeconds() + MAX_FUTURE_TIME_SECONDS);
      if (IsTimestampPremature)
      {
        throw new ChainException(ChainCode.INVALID);
      }
    }
    void ReorganizeChain(Chain chain)
    {
      SecondaryChains.Remove(chain);
      SecondaryChains.Add(MainChain);
      MainChain = chain;

      Locator.Reorganize();
    }

    public ChainHeader ReadHeader(byte[] headerHash, SHA256 sHA256Generator)
    {
      int key = BitConverter.ToInt32(headerHash, 0);

      lock (HeaderIndexLOCK)
      {
        if (HeaderIndex.TryGetValue(key, out List<ChainHeader> headers))
        {
          foreach (ChainHeader header in headers)
          {
            if (headerHash.IsEqual(header.GetHeaderHash(sHA256Generator)))
            {
              return header;
            }
          }
        }
      }

      throw new ChainException(string.Format("Header hash {0} not in chain.",
        headerHash.ToHexString()));
    }

    void UpdateHeaderIndex(ChainHeader header, byte[] headerHash)
    {
      int keyHeader = BitConverter.ToInt32(headerHash, 0);

      lock (HeaderIndexLOCK)
      {
        if (!HeaderIndex.TryGetValue(keyHeader, out List<ChainHeader> headers))
        {
          headers = new List<ChainHeader>();
          HeaderIndex.Add(keyHeader, headers);
        }

        headers.Add(header);
      }
    }
    
    public async Task LoadFromArchiveAsync()
    {
      try
      {
        using (var archiveReader = new HeaderReader())
        {
          NetworkHeader header = archiveReader.GetNextHeader();

          while (header != null)
          {
            await InsertHeaderAsync(header);
            header = archiveReader.GetNextHeader();
          }

          //int countHeader = 0;
          //while (header != null && countHeader < 250000)
          //{
          //  countHeader += 1;
          //  await InsertHeaderAsync(header);
          //  header = archiveReader.GetNextHeader();
          //}
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
