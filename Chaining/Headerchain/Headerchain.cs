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
    public enum BlockCode { ORPHAN, DUPLICATE, INVALID, PREMATURE };

    partial class Headerchain
    {
      static Chain MainChain;
      static List<Chain> SecondaryChains = new List<Chain>();
      static ChainHeader GenesisHeader;
      static List<ChainLocation> Checkpoints;

      Blockchain Blockchain;

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

        Locator = new HeaderLocator();
        Blockchain = blockchain;

        Inserter = new ChainInserter(MainChain, this);
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
          throw new ChainException("Received signal available but could not dispatch.");
        }
      }
      static void ValidateHeader(NetworkHeader header, out UInt256 headerHash)
      {
        headerHash = header.GetHeaderHash();

        if (headerHash.IsGreaterThan(UInt256.ParseFromCompact(header.NBits)))
        {
          throw new ChainException(BlockCode.INVALID);
        }

        const long MAX_FUTURE_TIME_SECONDS = 2 * 60 * 60;
        bool IsTimestampPremature = header.UnixTimeSeconds > (DateTimeOffset.UtcNow.ToUnixTimeSeconds() + MAX_FUTURE_TIME_SECONDS);
        if (IsTimestampPremature)
        {
          throw new ChainException(BlockCode.PREMATURE);
        }
      }
      void ReorganizeChain(Chain chain)
      {
        SecondaryChains.Remove(chain);
        SecondaryChains.Add(MainChain);
        MainChain = chain;

        Locator.Reorganize();
      }

      public HeaderStreamer GetHeaderStreamer()
      {
        return new HeaderStreamer(MainChain);
      }
      public HeaderInserter GetHeaderInserter()
      {
        return new HeaderInserter(this);
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
    }
  }
}
