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
    IPayloadParser PayloadParser;

    CheckpointManager Checkpoints;
    BlockchainController Controller;
    Chain MainChain;
    List<Chain> SecondaryChains = new List<Chain>();
    //BlockPayloadLocator BlockLocator;

    private readonly object lockBlockInsertion = new object();


    public Blockchain(
      ChainBlock genesisBlock,
      Network network,
      IPayloadParser payloadParser,
      List<BlockLocation> checkpoints)
    {
      PayloadParser = payloadParser;

      Checkpoints = new CheckpointManager(checkpoints);
      Controller = new BlockchainController(network, this);
      MainChain = new Chain(genesisBlock);

      //BlockLocator = new BlockPayloadLocator(this);
    }

    public async Task StartAsync()
    {
      await Controller.StartAsync();
    }

    public List<BlockLocation> GetBlockLocations() => MainChain.GetBlockLocations();
    
    Chain GetChain(UInt256 hash)
    {
      if (MainChain.Probe.GotoBlock(hash))
      {
        return MainChain;
      }

      foreach(Chain chain in SecondaryChains)
      {
        if (chain.Probe.GotoBlock(hash))
        {
          return chain;
        }
      }

      throw new BlockchainException(BlockCode.ORPHAN);
    }

    void InsertHeader(NetworkHeader header)
    {
      InsertBlock(new ChainBlock(header));
    }
    void InsertBlock(ChainBlock chainBlock)
    {
      UInt256 headerHash = new UInt256(Hashing.SHA256d(chainBlock.Header.GetBytes()));

      lock (lockBlockInsertion)
      {
        // create the probe on the fly so this becomes thread safe
        Chain chain = GetChain(chainBlock.Header.HashPrevious);
        
        ValidateCheckpoint(chain, headerHash);

        if (chain.Probe.IsTip())
        {
          chain.Socket.ExtendChain(chainBlock, headerHash);
        }
        else
        {
          chain = chain.Probe.ForkChain(chainBlock, headerHash);
          SecondaryChains.Add(chain);
        }

        if (chain.IsStrongerThan(MainChain))
        {
          MainChain.ReorganizeChain(chain);
        }
      }
    }
    void ValidateCheckpoint(Chain chain, UInt256 headerHash)
    {
      uint nextBlockHeight = chain.Probe.GetHeight() + 1;

      bool chainLongerThanHighestCheckpoint = chain.GetHeight() >= Checkpoints.HighestCheckpointHight;
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
    void InsertBlock(NetworkBlock networkBlock, BlockStore payloadStoreID)
    {
      var chainBlock = new ChainBlock(networkBlock.Header);
      InsertBlock(chainBlock);
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
    
    uint GetHeight() => MainChain.GetHeight();

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
