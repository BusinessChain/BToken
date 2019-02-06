using System.Diagnostics;

using System;
using System.Linq;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Threading.Tasks.Dataflow;

using BToken.Networking;


namespace BToken.Chaining
{
  public enum ChainCode { ORPHAN, DUPLICATE, INVALID, PREMATURE };

  public partial class Headerchain
  {
    Chain MainChain;
    List<Chain> SecondaryChains = new List<Chain>();
    ChainHeader GenesisHeader;
    List<ChainLocation> Checkpoints;

    Network Network;
    RequestListener Listener;

    Dictionary<byte[], List<ChainHeader>> HeaderIndex;
    int NumberHeaderIndexBytes = 4;

    HeaderLocator LocatorMainChain;
    HeaderArchiver Archiver = new HeaderArchiver();

    BufferBlock<bool> SignalInserterAvailable = new BufferBlock<bool>();
    ChainInserter Inserter;


    public Headerchain(
      NetworkHeader genesisHeader,
      Network network,
      List<ChainLocation> checkpoints)
    {
      GenesisHeader = new ChainHeader(genesisHeader, null);
      Checkpoints = checkpoints;
      MainChain = new Chain(GenesisHeader, 0, 0);

      HeaderIndex = new Dictionary<byte[], List<ChainHeader>>(new EqualityComparerByteArray());
      LocatorMainChain = new HeaderLocator(this);
      Network = network;
      Listener = new RequestListener(this, Network);

      Inserter = new ChainInserter(this);
    }
       
    public async Task StartAsync()
    {
      await LoadFromArchiveAsync();
      Console.WriteLine("Loaded headerchain from archive, height = '{0}'", MainChain.Height);

      Task listenerTask = Listener.StartAsync();
      Console.WriteLine("Inbound request listener started...");

      await InitialHeaderDownloadAsync();
      Console.WriteLine("Synchronized headerchain with network, height = '{0}'", MainChain.Height);
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

      LocatorMainChain.Reorganize();
    }

    void UpdateHeaderIndex(ChainHeader header, UInt256 headerHash)
    {
      byte[] keyHeader = headerHash.GetBytes().Take(NumberHeaderIndexBytes).ToArray();

      if (!HeaderIndex.TryGetValue(keyHeader, out List<ChainHeader> headers))
      {
        headers = new List<ChainHeader>();
        HeaderIndex.Add(keyHeader, headers);
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
      if (HeaderIndex.TryGetValue(keyHeaderIndex, out List<ChainHeader> headers))
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
      return new HeaderReader(this);
    }
    public HeaderWriter GetHeaderInserter()
    {
      return new HeaderWriter(this);
    }
    public List<UInt256> GetHeaderLocator()
    {
      return LocatorMainChain.GetHeaderLocator();
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

    async Task InitialHeaderDownloadAsync()
    {
      await Network.ExecuteSessionAsync(new SessionHeaderDownload(this));
    }

  }
}
