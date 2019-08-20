using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Threading.Tasks.Dataflow;
using System.IO;
using System.Security.Cryptography;

using BToken.Networking;
using BToken.Hashing;


namespace BToken.Chaining
{
  public partial class Blockchain
  {
    public enum ChainCode { ORPHAN, DUPLICATE, INVALID };
    
    public partial class Headerchain
    {
      Network Network;
      Chain MainChain;
      List<Chain> SecondaryChains = new List<Chain>();
      public Header GenesisHeader;
      List<HeaderLocation> Checkpoints;

      readonly object HeaderIndexLOCK = new object();
      Dictionary<int, List<Header>> HeaderIndex;

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
        Header genesisHeader,
        List<HeaderLocation> checkpoints,
        Network network)
      {
        Network = network;
        GenesisHeader = genesisHeader;
        Checkpoints = checkpoints;
        MainChain = new Chain(GenesisHeader, 0, 0);

        HeaderIndex = new Dictionary<int, List<Header>>();
        UpdateHeaderIndex(GenesisHeader);

        Locator = new HeaderLocator(this);

        Inserter = new ChainInserter(this);
      }


      public async Task StartAsync()
      {
        var headerSession = new SessionHeaderDownload(this, Network);
        await headerSession.StartAsync();

        Console.WriteLine("downloaded headerchain from network, height '{0}'",
          GetHeight());
      }
         
      public async Task InsertHeadersAsync(
        HeaderWriter archiveWriter,
        List<Header> headers)
      {
        var headersInserted = new List<byte[]>();

        foreach (Header header in headers)
        {
          try
          {
            await InsertHeaderAsync(header);
          }
          catch (ChainException ex)
          {
            Console.WriteLine(string.Format("Insertion of header with hash '{0}' raised ChainException '{1}'.",
              header.HeaderHash.ToHexString(),
              ex.Message));

            return;
          }

          archiveWriter.StoreHeader(header);
        }

        return;
      }
      async Task InsertHeaderAsync(Header header)
      {
        ValidateHeader(header);

        using (var inserter = await DispatchInserterAsync())
        {
          Chain rivalChain = inserter.InsertHeader(header);

          if (rivalChain != null && rivalChain.IsStrongerThan(MainChain))
          {
            ReorganizeChain(rivalChain);
          }
        }
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
      static void ValidateHeader(Header header)
      {
        if (header.HeaderHash.IsGreaterThan(header.NBits))
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

      public async Task LoadAsync()
      {
        try
        {
          byte[] headerBuffer = File.ReadAllBytes(FilePath);

          var sHA256 = SHA256.Create();

          int index = 0;

          while (index < headerBuffer.Length)
          {
            await InsertHeaderAsync(
              Header.ParseHeader(
                headerBuffer,
                ref index,
                sHA256));
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

}
