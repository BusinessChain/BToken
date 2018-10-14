using System.Diagnostics;

using System;
using System.Threading;
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

    SocketProbe ProbeMain;



    public Blockchain(
      string genesisBlockString,
      IBlockchainNetwork network,
      IBlockPayloadParser payloadParser,
      List<BlockLocation> checkpoints)
    {
      Controller = new BlockchainController(network, this);

      PayloadParser = payloadParser;

      Checkpoints = new CheckpointManager(checkpoints);

      ChainBlock genesisBlock = CreateGenesisBlock(genesisBlockString);
      ProbeMain = new SocketProbe(
        blockchain: this,
        genesisBlock: genesisBlock);

    }
    ChainBlock CreateGenesisBlock(string genesisBlockString)
    {

    }

    public async Task StartAsync()
    {
      await Controller.StartAsync();
    }

    public List<BlockLocation> GetBlockLocations() => ProbeMain.GetBlockLocations();

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
    //  catch(BlockchainException ex)
    //  {
    //    if(ex.ErrorCode == BlockCode.ORPHAN)
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
    //    SocketProbe probe = GetProbe(hash);
    //    return probe.Block;
    //  }
    //  catch (BlockchainException)
    //  {
    //    return null;
    //  }
    //}

    SocketProbe GetProbe(UInt256 hash)
    {
      SocketProbe probe = ProbeMain;

      while (true)
      {
        if(probe == null)
        {
          throw new BlockchainException(BlockCode.ORPHAN);
        }
        
        if (probe.GetAtBlock(hash))
        {
          return probe;
        }

        probe = probe.GetProbeWeaker();
      }
    }

    void InsertHeader(NetworkHeader header, UInt256 headerHash)
    {
      var chainBlock = new ChainBlock(header);
      InsertBlock(chainBlock, headerHash);
    }
    void InsertBlock(ChainBlock chainBlock, UInt256 headerHash)
    {
      SocketProbe probeAtBlockPrevious = GetProbe(chainBlock.Header.HashPrevious);
      ValidateCheckpoint(probeAtBlockPrevious, headerHash);
      probeAtBlockPrevious.InsertBlock(chainBlock, headerHash);
    }
    void ValidateCheckpoint(SocketProbe probe, UInt256 headerHash)
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

    void InsertProbe(SocketProbe probe)
    {
      if (probe.IsStrongerThan(ProbeMain))
      {
        probe.ConnectAsProbeWeaker(ProbeMain);
        ProbeMain = probe;
      }
      else
      {
        ProbeMain.InsertProbeRecursive(probe);
      }
    }

    uint GetHeight() => ProbeMain.GetHeightTip();

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
      SocketProbe probe = ProbeMain;

      do
      {
        blocksUnassignedPayload.AddRange(probe.GetBlocksUnassignedPayload(batchSize));
        batchSize -= blocksUnassignedPayload.Count;
        probe = probe.GetProbeWeaker();
      } while (batchSize > 0 && probe != null);

      return blocksUnassignedPayload;
    }
  }
}
