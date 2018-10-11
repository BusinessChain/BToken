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
    static IBlockPayloadParser PayloadParser;

    ChainBlock GenesisBlock;
    UInt256 GenesisBlockHash;

    CheckpointManager Checkpoints;

    ChainSocket SocketMain;


    public Blockchain(
      IBlockPayloadParser payloadParser,
      ChainBlock genesisBlock, 
      List<BlockLocation> checkpoints)
    {
      PayloadParser = payloadParser;
      GenesisBlock = genesisBlock;
      GenesisBlockHash = new UInt256(Hashing.SHA256d(genesisBlock.Header.getBytes()));

      Checkpoints = new CheckpointManager(checkpoints);

      SocketMain = new ChainSocket(
        blockchain: this,
        blockGenesis: genesisBlock,
        blockGenesisHash: GenesisBlockHash);
    }
    
    public List<BlockLocation> GetBlockLocations() => SocketMain.GetBlockLocations();

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

    public ChainBlock GetBlock(UInt256 hash)
    {
      try
      {
        ChainSocket.SocketProbe probe = GetProbe(hash);
        return probe.Block;
      }
      catch (BlockchainException)
      {
        return null;
      }
    }

    ChainSocket.SocketProbe GetProbe(UInt256 hash)
    {
      ChainSocket socket = SocketMain;

      while (true)
      {
        if(socket == null)
        {
          throw new BlockchainException(BlockCode.ORPHAN);
        }
        
        if (socket.LocateProbeAtBlock(hash))
        {
          return socket.Probe;
        }

        socket = socket.SocketWeaker;
      }
    }

    public void InsertHeader(NetworkHeader header, UInt256 headerHash)
    {
      var chainBlock = new ChainBlock(header);
      InsertBlock(chainBlock, headerHash);
    }
    void InsertBlock(ChainBlock chainBlock, UInt256 headerHash)
    {
      ChainSocket.SocketProbe probeAtBlockPrevious = GetProbe(chainBlock.Header.HashPrevious);
      ValidateCheckpoint(probeAtBlockPrevious, headerHash);
      probeAtBlockPrevious.InsertBlock(chainBlock, headerHash);
    }
    void ValidateCheckpoint(ChainSocket.SocketProbe probe, UInt256 headerHash)
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
    public void InsertBlock(NetworkBlock networkBlock, UInt256 headerHash, BlockArchiver.BlockStore payloadStoreID)
    {
      var chainBlock = new ChainBlock(networkBlock.Header);
      InsertBlock(chainBlock, headerHash);
      InsertPayload(chainBlock, networkBlock.Payload, payloadStoreID);
    }

    public static void InsertPayload(ChainBlock chainBlock, byte[] payload, BlockArchiver.BlockStore payloadStoreID)
    {
      ValidatePayload(chainBlock, payload);
      chainBlock.BlockStore = payloadStoreID;
    }
    static void ValidatePayload(ChainBlock chainBlock, byte[] payload)
    {
      UInt256 payloadHash = PayloadParser.GetPayloadHash(payload);
      if (!payloadHash.IsEqual(chainBlock.Header.PayloadHash))
      {
        throw new BlockchainException(BlockCode.INVALID);
      }
    }

    void InsertSocket(ChainSocket socket)
    {
      if (socket.IsStrongerThan(SocketMain))
      {
        socket.ConnectAsSocketWeaker(SocketMain);
        SocketMain = socket;
      }
      else
      {
        SocketMain.InsertSocketRecursive(socket);
      }
    }

    public uint GetHeight() => SocketMain.BlockTipHeight;

    static ChainBlock GetBlockPrevious(ChainBlock block, uint depth)
    {
      if (depth == 0 || block.BlockPrevious == null)
      {
        return block;
      }

      return GetBlockPrevious(block.BlockPrevious, --depth);
    }
    
    public List<ChainBlock> GetBlocksUnassignedPayload(int batchSize)
    {
      var blocksUnassignedPayload = new List<ChainBlock>();
      ChainSocket socket = SocketMain;

      do
      {
        blocksUnassignedPayload.AddRange(socket.GetBlocksUnassignedPayload(batchSize));
        batchSize -= blocksUnassignedPayload.Count;
        socket = socket.SocketWeaker;
      } while (batchSize > 0 && socket != null);

      return blocksUnassignedPayload;
    }
  }
}
