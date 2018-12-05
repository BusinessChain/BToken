using System.Diagnostics;

using System;
using System.Linq;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.IO;

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


    public Headerchain(
      NetworkHeader genesisHeader,
      INetwork network,
      List<ChainLocation> checkpoints,
      Blockchain blockchain)
    {
      Controller = new HeaderchainController(network, this, Archiver);
      MainChain = new Chain(new ChainHeader(genesisHeader, null));

      Validator = new HeaderValidator(checkpoints);
      Locator = new HeaderLocator(this);
      Blockchain = blockchain;
    }

    public async Task StartAsync()
    {
      await Controller.StartAsync();
    }

    public void InsertBlock(NetworkHeader header, IPayloadParser payloadParser)
    {
      ChainProbe probe = GetProbeAtHeader(header.HashPrevious);

      ValidateBlock(probe, header, out UInt256 headerHash, payloadParser);

      var inserter = new ChainInserter(probe);

      inserter.ConnectHeader(header);

      if (probe.IsTip())
      {
        inserter.ExtendChain(headerHash);

        if (probe.Chain == MainChain)
        {
          Locator.Update();
          return;
        }
      }
      else
      {
        inserter.ForkChain(headerHash);
        SecondaryChains.Add(probe.Chain);
      }

      if (probe.Chain.IsStrongerThan(MainChain))
      {
        ReorganizeChain(probe.Chain);
      }
    }
    void InsertHeader(NetworkHeader header)
    {
      InsertBlock(header, null);
    }
    void ValidateBlock(ChainProbe probe, NetworkHeader header, out UInt256 headerHash, IPayloadParser payloadParser)
    {
      Validator.ValidateHeader(probe, header, out headerHash);

      if (payloadParser != null)
      {
        payloadParser.ValidatePayload();
      }
    }
    ChainProbe GetProbeAtHeader(UInt256 hash)
    {
      var probe = new ChainProbe(MainChain);

      if (probe.GoTo(hash))
      {
        return probe;
      }

      foreach (Chain chain in SecondaryChains)
      {
        probe = new ChainProbe(chain);

        if (probe.GoTo(hash))
        {
          return probe;
        }
      }

      return null;
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
