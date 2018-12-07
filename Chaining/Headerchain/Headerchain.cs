using System.Diagnostics;

using System;
using System.Linq;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Threading.Tasks.Dataflow;

using BToken.Networking;


namespace BToken.Chaining
{
  public enum BlockCode { ORPHAN, DUPLICATE, INVALID, PREMATURE };


  partial class Headerchain
  {
    Chain MainChain;
    List<Chain> SecondaryChains = new List<Chain>();

    HeaderValidator Validator;
    HeaderLocator Locator;

    HeaderchainController Controller;
    HeaderArchiver Archiver = new HeaderArchiver();
    Blockchain Blockchain;

    BufferBlock<bool> SignalInserterAvailable = new BufferBlock<bool>();
    ChainInserter Inserter;


    public Headerchain(
      NetworkHeader genesisHeader,
      INetwork network,
      List<ChainLocation> checkpoints,
      Blockchain blockchain)
    {
      Controller = new HeaderchainController(network, this, Archiver);
      MainChain = new Chain(new ChainHeader(genesisHeader, null), 0, 0);

      Locator = new HeaderLocator(this);
      Blockchain = blockchain;

      Inserter = new ChainInserter(MainChain, this, checkpoints);
    }

    public async Task StartAsync()
    {
      await Controller.StartAsync();
    }

    async Task InsertHeaderAsync(NetworkHeader header)
    {
      ValidateHeader(header, out UInt256 headerHash);

      using (var inserter = await DispatchInserterAsync())
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

  }
}
