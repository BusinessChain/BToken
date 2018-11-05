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


  public partial class Blockchain
  {
    BlockchainController Controller;
    Chain MainChain;
    List<Chain> SecondaryChains = new List<Chain>();
    
    BlockValidator Validator;
    BlockLocator Locator;
    BlockArchiver Archiver;

    private readonly object lockBlockInsertion = new object();


    public Blockchain(
      ChainBlock genesisBlock,
      Network network,
      List<BlockLocation> checkpoints)
    {
      Controller = new BlockchainController(network, this);
      MainChain = new Chain(genesisBlock);

      Validator = new BlockValidator(checkpoints);
      Locator = new BlockLocator(this);
      Archiver = new BlockArchiver(this);
    }

    public async Task StartAsync()
    {
      await Controller.StartAsync();
    }

    public List<BlockLocation> GetBlockLocations() => Locator.BlockLocations;
    
    void InsertHeader(NetworkHeader header)
    {
      InsertBlock(new ChainBlock(header));
    }
    void InsertBlock(ChainBlock block)
    {
      ChainProbe probe = GetChainProbe(block.Header.HashPrevious);

      Validator.Validate(probe, block, out UInt256 headerHash);

      probe.ConnectBlock(block);

      if (probe.IsTip())
      {
        probe.Chain.ExtendChain(block, headerHash);

        if (probe.Chain == MainChain)
        {
          Locator.Update();
          return;
        }
      }
      else
      {
        probe.ForkChain(block, headerHash);
        SecondaryChains.Add(probe.Chain);
      }

      if (probe.Chain.IsStrongerThan(MainChain))
      {
        ReorganizeChain(probe.Chain);
      }
    }
    ChainProbe GetChainProbe(UInt256 hash)
    {
      var probe = new ChainProbe(MainChain);

      if (probe.GotoBlock(hash))
      {
        return probe;
      }

      foreach (Chain chain in SecondaryChains)
      {
        probe.Chain = chain;

        if (probe.GotoBlock(hash))
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
        
    uint GetHeight() => MainChain.Height;

    static ChainBlock GetBlockPrevious(ChainBlock block, uint depth)
    {
      if (depth == 0 || block.BlockPrevious == null)
      {
        return block;
      }

      return GetBlockPrevious(block.BlockPrevious, --depth);
    }

    List<ChainBlock> GetBlocksUnassignedPayload(int batchSize)
    {
      var blocksUnassignedPayload = new List<ChainBlock>();
      Chain chain = MainChain;

      //do
      //{
      //  blocksUnassignedPayload.AddRange(chain.GetBlocksUnassignedPayload(batchSize));
      //  batchSize -= blocksUnassignedPayload.Count;
      //  chain = chain.GetChainWeaker();
      //} while (batchSize > 0 && chain != null);

      return blocksUnassignedPayload;
    }

  }
}
