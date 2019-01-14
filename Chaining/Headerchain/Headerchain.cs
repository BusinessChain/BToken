using System.Diagnostics;

using System;
using System.Linq;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Threading.Tasks.Dataflow;

using BToken.Networking;


namespace BToken.Chaining
{
  public partial class Blockchain
  {
    public enum ChainCode { ORPHAN, DUPLICATE, INVALID, PREMATURE };

    partial class Headerchain
    {
      Chain MainChain;
      List<Chain> SecondaryChains = new List<Chain>();
      ChainHeader GenesisHeader;
      List<ChainLocation> Checkpoints;

      Blockchain Blockchain;

      Dictionary<byte[], List<ChainHeader>> IndexTable;
      HeaderLocator Locator;
      HeaderArchiver Archiver = new HeaderArchiver();

      BufferBlock<bool> SignalInserterAvailable = new BufferBlock<bool>();
      ChainInserter Inserter;


      public Headerchain(
        NetworkHeader genesisHeader,
        INetwork network,
        List<ChainLocation> checkpoints,
        Blockchain blockchain)
      {
        GenesisHeader = new ChainHeader(genesisHeader, null);
        Checkpoints = checkpoints;
        MainChain = new Chain(GenesisHeader, 0, 0);

        IndexTable = new Dictionary<byte[], List<ChainHeader>>(new EqualityComparerByteArray());
        Locator = new HeaderLocator(this);
        Blockchain = blockchain;

        Inserter = new ChainInserter(this);
      }
      
      async Task InsertHeaderAsync(NetworkHeader header)
      {
        ValidateHeader(header, out UInt256 headerHash);

        using (Inserter = await DispatchInserterAsync())
        {
          Chain rivalChain = Inserter.InsertHeader(header, headerHash);

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
      static void ValidateHeader(NetworkHeader header, out UInt256 headerHash)
      {
        headerHash = header.ComputeHeaderHash();

        // Probably a bug: the header hash should be equal to NBits
        if (headerHash.IsGreaterThan(UInt256.ParseFromCompact(header.NBits)))
        {
          throw new ChainException(ChainCode.INVALID);
        }

        const long MAX_FUTURE_TIME_SECONDS = 2 * 60 * 60;
        bool IsTimestampPremature = header.UnixTimeSeconds > (DateTimeOffset.UtcNow.ToUnixTimeSeconds() + MAX_FUTURE_TIME_SECONDS);
        if (IsTimestampPremature)
        {
          throw new ChainException(ChainCode.PREMATURE);
        }
      }
      void ReorganizeChain(Chain chain)
      {
        SecondaryChains.Remove(chain);
        SecondaryChains.Add(MainChain);
        MainChain = chain;

        Locator.Reorganize();
      }

      void UpdateHeaderIndex()
      {
        byte[] keyHeader = MainChain.HeaderTipHash.GetBytes().Take(4).ToArray();
        ChainHeader header = MainChain.HeaderTip;

        if (!IndexTable.TryGetValue(keyHeader, out List<ChainHeader> headers))
        {
          headers = new List<ChainHeader>();
          IndexTable.Add(keyHeader, headers);
        }
        headers.Add(header);
      }

      public static bool TryGetHeaderHash(ChainHeader header, out UInt256 headerHash)
      {        
        if (header.HeadersNext.Any())
        {
          headerHash = header.HeadersNext[0].NetworkHeader.HashPrevious;
          return true;
        }
        else
        {
          headerHash = null;
          return false;
        }
      }

      public List<ChainHeader> ReadHeaders(byte[] keyHeaderIndex)
      {
        if(IndexTable.TryGetValue(keyHeaderIndex, out List<ChainHeader> headers))
        {
          return headers;
        }
        else
        {
          return new List<ChainHeader>();
        }
      }
      public HeaderReader GetHeaderReader()
      {
        return new HeaderReader(MainChain, GenesisHeader);
      }
      public HeaderWriter GetHeaderInserter()
      {
        return new HeaderWriter(this);
      }
      public List<UInt256> GetHeaderLocator()
      {
        return Locator.GetHeaderLocator();
      }
      public async Task LoadFromArchiveAsync()
      {
        try
        {
          using (var archiveReader = Archiver.GetReader())
          {
            NetworkHeader header = archiveReader.GetNextHeader();

            while (header != null)
            {
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

      public async Task InitialHeaderDownloadAsync()
      {
        await Blockchain.Network.ExecuteSessionAsync(new SessionHeaderDownload(this));
      }
      
      public uint GetHeight()
      {
        return MainChain.Height;
      }
    }
  }
}
