using System;
using System.Linq;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Threading.Tasks.Dataflow;
using System.IO;
using System.Runtime.Remoting.Metadata.W3cXsd2001;

using BToken.Networking;


namespace BToken.Chaining
{
  public enum ChainCode { ORPHAN, DUPLICATE, INVALID };

  public partial class Headerchain
  {
    Chain MainChain;
    List<Chain> SecondaryChains = new List<Chain>();
    ChainHeader GenesisHeader;
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
      UpdateHeaderIndex(GenesisHeader, GetHeaderHash(GenesisHeader));

      Locator = new HeaderLocator(this);

      Inserter = new ChainInserter(this);
    }

    public List<NetworkHeader> GetHeaders(List<UInt256> headerLocator, UInt256 stopHash)
    {
      HeaderStream headerStreamer = new HeaderStream(this);
      var headers = new List<NetworkHeader>();
      
      return headers;
    }

    public async Task<List<UInt256>> InsertHeadersAsync(List<NetworkHeader> headers)
    {
      using (var archiveWriter = new HeaderWriter())
      {
        return await InsertHeadersAsync(archiveWriter, headers);
      }
    }
    public async Task<List<UInt256>> InsertHeadersAsync(HeaderWriter archiveWriter, List<NetworkHeader> headers)
    {
      var headersInserted = new List<UInt256>();

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
    async Task<UInt256> InsertHeaderAsync(NetworkHeader header)
    {
      ValidateHeader(header, out UInt256 headerHash);

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
    static void ValidateHeader(NetworkHeader header, out UInt256 headerHash)
    {
      string merkleRoot = new SoapHexBinary(header.MerkleRoot).ToString();
      headerHash = header.ComputeHash();

      if (headerHash.IsGreaterThan(UInt256.ParseFromCompact(header.NBits)))
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

    public ChainHeader ReadHeader(UInt256 headerHash, byte[] headerHashBytes)
    {
      int key = BitConverter.ToInt32(headerHashBytes, 0);

      lock (HeaderIndexLOCK)
      {
        if (HeaderIndex.TryGetValue(key, out List<ChainHeader> headers))
        {
          foreach (ChainHeader header in headers)
          {
            if (headerHash.Equals(GetHeaderHash(header)))
            {
              return header;
            }
          }
        }
      }

      throw new ChainException(string.Format("Header hash {0} not in chain.",
        headerHash));
    }
    public bool TryReadHeaders(int keyHeaderIndex, out List<ChainHeader> headers)
    {
      lock(HeaderIndexLOCK)
      {
        return HeaderIndex.TryGetValue(keyHeaderIndex, out headers);
      }
    }

    void UpdateHeaderIndex(ChainHeader header, UInt256 headerHash)
    {
      int keyHeader = BitConverter.ToInt32(headerHash.GetBytes(), 0);

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

    public static UInt256 GetHeaderHash(ChainHeader header)
    {
      if (header.HeadersNext != null)
      {
        return header.HeadersNext[0].NetworkHeader.HashPrevious;
      }

      return header.NetworkHeader.ComputeHash();
    }
    
    public async Task LoadFromArchiveAsync()
    {
      try
      {
        using (var archiveReader = new HeaderReader())
        {
          NetworkHeader header = archiveReader.GetNextHeader();

          //while (header != null)
          //{
          //  await InsertHeaderAsync(header);
          //  header = archiveReader.GetNextHeader();
          //}

          int countHeader = 0;
          while (header != null && countHeader < 200000)
          {
            countHeader++;
            await InsertHeaderAsync(header);
            header = archiveReader.GetNextHeader();
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
