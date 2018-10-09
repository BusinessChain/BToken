﻿using System.Diagnostics;

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
    ChainBlock GenesisBlock;
    UInt256 GenesisBlockHash;

    CheckpointManager Checkpoints;

    ChainSocket SocketMain;


    public Blockchain( 
      ChainBlock genesisBlock, 
      List<BlockLocation> checkpoints)
    {
      GenesisBlock = genesisBlock;
      GenesisBlockHash = new UInt256(Hashing.SHA256d(genesisBlock.Header.getBytes()));

      Checkpoints = new CheckpointManager(checkpoints);

      SocketMain = new ChainSocket(
        blockchain: this,
        blockGenesis: genesisBlock,
        blockGenesisHash: GenesisBlockHash);
    }
    
    public List<BlockLocation> GetBlockLocations() => SocketMain.GetBlockLocations();

    public static Blockchain Merge(Blockchain chain1, Blockchain chain2)
    {
      try
      {
        //InsertBlock funzt nur für einzelne Blöcke
        chain1.InsertBlock(chain2.GenesisBlock, chain2.GenesisBlockHash);

        // Deshalb muss hier entweder iterativ alle Blöcke in den anderen Strang eingflügt werden
        // Oder aber man schreibt einen speziellen Chainmerger was zu bevorzugen ist.

        return chain1;
      }
      catch(BlockchainException ex)
      {
        if(ex.ErrorCode == BlockCode.ORPHAN)
        {
          chain2.InsertBlock(chain1.GenesisBlock, chain1.GenesisBlockHash);
          return chain2;
        }

        throw ex;
      }
    }

    public ChainBlock GetBlock(UInt256 hash)
    {
      try
      {
        ChainSocket socket = GetSocket(hash);
        return socket.Probe.Block;
      }
      catch (BlockchainException)
      {
        return null;
      }
    }

    ChainSocket GetSocket(UInt256 hash)
    {
      ChainSocket socket = SocketMain;

      while (true)
      {
        if(socket == null)
        {
          throw new BlockchainException(BlockCode.ORPHAN);
        }
        
        if (socket.GetProbeAtBlock(hash) != null)
        {
          return socket;
        }

        socket = socket.SocketWeaker;
      }
    }

    public void InsertBlock(ChainBlock block, UInt256 headerHash)
    {
      ChainSocket socketBlockPrevious = GetSocket(block.Header.HashPrevious);

      socketBlockPrevious.InsertBlock(block, headerHash);
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
