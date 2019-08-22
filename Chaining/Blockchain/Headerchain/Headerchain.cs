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

      public HeaderLocator Locator;

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
         
      public void InsertHeaders(List<Header> headers)
      {
        foreach (Header header in headers)
        {
          try
          {
            InsertHeader(header);
          }
          catch (ChainException ex)
          {
            Console.WriteLine(string.Format("Insertion of header with hash '{0}' raised ChainException '{1}'.",
              header.HeaderHash.ToHexString(),
              ex.Message));

            throw ex;
          }
        }

        return;
      }
      void InsertHeader(Header header)
      {
        ValidateHeader(header);

        Chain rivalChain = Inserter.InsertHeader(header);

        if (rivalChain != null && rivalChain.IsStrongerThan(MainChain))
        {
          ReorganizeChain(rivalChain);
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

      public void Load()
      {
        try
        {
          byte[] headerBuffer = File.ReadAllBytes(FilePath);

          var sHA256 = SHA256.Create();

          int index = 0;

          while (index < headerBuffer.Length)
          {
            InsertHeader(
              Header.ParseHeader(
                headerBuffer,
                ref index,
                sHA256));
          }

          Console.WriteLine("Loaded chain, hight {0}, hash: {1}", 
            MainChain.Height,
            MainChain.HeaderTipHash.ToHexString());
        }
        catch (Exception ex)
        {
          Console.WriteLine(ex.Message);
        }
      }
    }
  }

}
