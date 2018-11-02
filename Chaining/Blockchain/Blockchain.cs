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

    ChainProbe GetChainProbe(UInt256 hash)
    {
      var probe = new ChainProbe(MainChain);

      if (probe.GotoBlock(hash))
      {
        return probe;
      }

      foreach(Chain chain in SecondaryChains)
      {
        probe.Chain = chain;

        if (probe.GotoBlock(hash))
        {
          return probe;
        }
      }

      return null;
    }

    void InsertHeader(NetworkHeader header)
    {
      InsertBlock(new ChainBlock(header));
    }
    void InsertBlock(ChainBlock block)
    {
      ChainProbe probe = GetChainProbe(block.Header.HashPrevious);

      Validate(probe, block, out UInt256 headerHash);

      ConnectChainBlock(block);

      if (probe.IsTip())
      {
        probe.Chain.Socket.ExtendChain(block, headerHash);
      }
      else
      {
        ForkChain(probe.Chain, block, headerHash);
        //SecondaryChains.Add(chain);
      }

      if (probe.Chain.IsStrongerThan(MainChain))
      {
        MainChain.ReorganizeChain(chain);
      }
    }
    void Validate(ChainProbe probe, ChainBlock block, out UInt256 headerHash)
    {
      if(probe == null)
      {
        throw new BlockchainException(BlockCode.ORPHAN);
      }

      ValidateTimeStamp(block.Header.UnixTimeSeconds);

      headerHash = new UInt256(Hashing.SHA256d(block.Header.GetBytes()));

      ValidateCheckpoint(probe, headerHash);

      ValidateUniqueness(headerHash);

      ValidateProofOfWork(block.Header.NBits, headerHash);

    }
    void ValidateCheckpoint(ChainProbe probe, UInt256 headerHash)
    {
      uint nextBlockHeight = probe.GetHeight() + 1;

      bool chainLongerThanHighestCheckpoint = probe.Chain.GetHeight() >= Checkpoints.HighestCheckpointHight;
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
    void ValidateProofOfWork(uint nBits, UInt256 headerHash)
    {
      if (headerHash.IsGreaterThan(UInt256.ParseFromCompact(nBits)))
      {
        throw new BlockchainException(BlockCode.INVALID);
      }

      if (nBits != TargetManager.GetNextTargetBits(this))
      {
        throw new BlockchainException(BlockCode.INVALID);
      }
    }
    void ValidateTimeStamp(uint unixTimeSeconds)
    {
      const long MAX_FUTURE_TIME_SECONDS = 2 * 60 * 60;
      bool IsTimestampPremature = (long)unixTimeSeconds > (DateTimeOffset.UtcNow.ToUnixTimeSeconds() + MAX_FUTURE_TIME_SECONDS);
      if (IsTimestampPremature)
      {
        throw new BlockchainException(BlockCode.PREMATURE);
      }

      if (unixTimeSeconds <= GetMedianTimePast())
      {
        throw new BlockchainException(BlockCode.INVALID);
      }
    }
    void ValidateUniqueness(UInt256 hash)
    {
      if (Block.BlocksNext.Any(b => Chain.GetHeaderHash(b).IsEqual(hash)))
      {
        throw new BlockchainException(BlockCode.DUPLICATE);
      }
    }
    uint GetMedianTimePast()
    {
      const int MEDIAN_TIME_PAST = 11;

      List<uint> timestampsPast = new List<uint>();
      ChainBlock block = Block;

      int depth = 0;
      while (depth < MEDIAN_TIME_PAST)
      {
        timestampsPast.Add(block.Header.UnixTimeSeconds);

        if (block.BlockPrevious == null)
        { break; }

        block = block.BlockPrevious;
        depth++;
      }

      timestampsPast.Sort();

      return timestampsPast[timestampsPast.Count / 2];
    }
    void ConnectChainBlock(ChainBlock block)
    {
      block.BlockPrevious = Block;
      Block.BlocksNext.Add(block);
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
