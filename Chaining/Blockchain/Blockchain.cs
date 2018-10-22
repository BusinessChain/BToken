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
    IBlockPayloadParser PayloadParser;
    
    CheckpointManager Checkpoints;

    Chain MainChain;
    BlockPayloadLocator BlockLocator;



    public Blockchain(
      ChainBlock genesisBlock,
      Network network,
      IBlockPayloadParser payloadParser,
      List<BlockLocation> checkpoints)
    {
      Controller = new BlockchainController(network, this);

      PayloadParser = payloadParser;

      Checkpoints = new CheckpointManager(checkpoints);

      MainChain = new Chain(
        blockchain: this,
        genesisBlock: genesisBlock);

      BlockLocator = new BlockPayloadLocator(this);

    }

    public async Task StartAsync()
    {
      await Controller.StartAsync();
    }

    public List<BlockLocation> GetBlockLocations() => MainChain.GetBlockLocations();

    //public static Blockchain Merge(Blockchain chain1, Blockchain chain2)
    //{
    //  try
    //  {
    //    //InsertBlock funzt nur für einzelne Blöcke
    //    chain1.InsertBlock(chain2.GenesisBlock, chain2.GenesisBlockHash);

    //    // Deshalb muss hier entweder iterativ alle Blöcke in den anderen Strang eingflügt werden
    //    // Oder aber man schreibt einen speziellen Chainmerger was zu bevorzugen ist.

    //    return chain1;
    //  }
    //  catch (BlockchainException ex)
    //  {
    //    if (ex.ErrorCode == BlockCode.ORPHAN)
    //    {
    //      chain2.InsertBlock(chain1.GenesisBlock, chain1.GenesisBlockHash);
    //      return chain2;
    //    }

    //    throw ex;
    //  }
    //}

    //public ChainBlock GetBlock(UInt256 hash)
    //{
    //  try
    //  {
    //    Chain chain = GetChain(hash);
    //    return chain.Block;
    //  }
    //  catch (BlockchainException)
    //  {
    //    return null;
    //  }
    //}

    Chain GetChain(UInt256 hash)
    {
      Chain chain = MainChain;

      while (true)
      {
        if(chain == null)
        {
          throw new BlockchainException(BlockCode.ORPHAN);
        }
        
        if (chain.GetAtBlock(hash))
        {
          return chain;
        }

        chain = chain.GetChainWeaker();
      }
    }

    void InsertHeader(NetworkHeader header, UInt256 headerHash)
    {
      var chainBlock = new ChainBlock(header);
      InsertBlock(chainBlock, headerHash);
    }
    void InsertBlock(ChainBlock chainBlock, UInt256 headerHash)
    {
      Chain probeAtBlockPrevious = GetChain(chainBlock.Header.HashPrevious);
      ValidateCheckpoint(probeAtBlockPrevious, headerHash);
      probeAtBlockPrevious.InsertBlock(chainBlock, headerHash);
    }
    void ValidateCheckpoint(Chain probe, UInt256 headerHash)
    {
      uint nextBlockHeight = probe.GetHeight() + 1;

      bool chainLongerThanHighestCheckpoint = probe.GetHeightTip() >= Checkpoints.HighestCheckpointHight;
      bool nextHeightBelowHighestCheckpoint = !(nextBlockHeight > Checkpoints.HighestCheckpointHight);

      if (chainLongerThanHighestCheckpoint && nextHeightBelowHighestCheckpoint)
      {
        throw new BlockchainException(BlockCode.INVALID);
      }

      if (!Checkpoints.ValidateBlockLocation(nextBlockHeight, headerHash))
      { 
        throw new BlockchainException(BlockCode.INVALID);
      }
    }
    void InsertBlock(NetworkBlock networkBlock, UInt256 headerHash, BlockStore payloadStoreID)
    {
      var chainBlock = new ChainBlock(networkBlock.Header);
      InsertBlock(chainBlock, headerHash);
      InsertPayload(chainBlock, networkBlock.Payload, payloadStoreID);
    }
    void InsertPayload(ChainBlock chainBlock, byte[] payload, BlockStore payloadStoreID)
    {
      ValidatePayload(chainBlock, payload);
      chainBlock.BlockStore = payloadStoreID;
    }
    void ValidatePayload(ChainBlock chainBlock, byte[] payload)
    {
      UInt256 payloadHash = PayloadParser.GetPayloadHash(payload);
      if (!payloadHash.IsEqual(chainBlock.Header.PayloadHash))
      {
        throw new BlockchainException(BlockCode.INVALID);
      }
    }

    void InsertChain(Chain chain)
    {
      if (chain.IsStrongerThan(MainChain))
      {
        chain.ConnectAsWeakerChain(MainChain);
        MainChain = chain;
      }
      else
      {
        MainChain.InsertChainRecursive(chain);
      }
    }

    uint GetHeight() => MainChain.GetHeightTip();

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

      do
      {
        blocksUnassignedPayload.AddRange(chain.GetBlocksUnassignedPayload(batchSize));
        batchSize -= blocksUnassignedPayload.Count;
        chain = chain.GetChainWeaker();
      } while (batchSize > 0 && chain != null);

      return blocksUnassignedPayload;
    }
  }
}
