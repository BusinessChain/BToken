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
    
    partial class Headerchain : IDatabase
    {
      Network Network;
      Chain MainChain;
      List<Chain> SecondaryChains = new List<Chain>();
      public Header GenesisHeader;
      List<HeaderLocation> Checkpoints;

      byte[] HeaderHashInsertedLast;

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
      static string FilePath = Path.Combine(RootDirectory.Name, "h");


      public Headerchain(
        Header genesisHeader,
        List<HeaderLocation> checkpoints,
        Network network)
      {
        Network = network;
        GenesisHeader = genesisHeader;
        Checkpoints = checkpoints;
        MainChain = new Chain(GenesisHeader, 0, 0);
        HeaderHashInsertedLast = GenesisHeader.HeaderHash;

        HeaderIndex = new Dictionary<int, List<Header>>();
        UpdateHeaderIndex(GenesisHeader);

        Locator = new HeaderLocator(this);
        Inserter = new ChainInserter(this);
      }


      
      public int LoadImage()
      {
        return 1;
      }



      int IndexBatchCreation = 1;

      public DataBatch CreateBatch()
      {
        return new HeaderBatch(IndexBatchCreation++);
      }


      
      public void InsertBatch(DataBatch batch)
      {
        if (!HeaderHashInsertedLast.IsEqual(((HeaderBatch)batch).GetHeaderHashPrevious()))
        {
          throw new ChainException(
            string.Format("HeaderPrevious {0} of Batch {1} not equal to \nHeaderMergedLast {2}",
            ((HeaderBatch)batch).GetHeaderHashPrevious().ToHexString(),
            batch.Index,
            HeaderHashInsertedLast.ToHexString()));
        }

        foreach (HeaderBatchContainer headerBatchContainer in batch.ItemBatchContainers)
        {
          foreach (Header header in headerBatchContainer.Headers)
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
        }

        HeaderHashInsertedLast = ((HeaderBatch)batch).GetHeaderHashLast();
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

      public DataBatch LoadBatchFromArchive(int batchIndex)
      {
        byte[] headerBytes = File.ReadAllBytes(FilePath + batchIndex);

        int startIndex = 0;
        var headers = new List<Header>();

        SHA256 sHA256 = SHA256.Create();
        int headersCount = VarInt.GetInt32(headerBytes, ref startIndex);
        for (int i = 0; i < headersCount; i += 1)
        {
          headers.Add(
            Header.ParseHeader(
              headerBytes, 
              ref startIndex, 
              sHA256));

          startIndex += 1; // skip txCount (always a zero-byte)
        }

        return new HeaderBatch(
          headers, 
          headerBytes, 
          batchIndex);
      }

      public async Task ArchiveBatchAsync(DataBatch batch)
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
    }
  }

}
