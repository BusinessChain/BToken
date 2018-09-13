﻿using System;
using System.Linq;
using System.Collections.Generic;
using System.Threading.Tasks;

using BToken.Networking;


namespace BToken.Chaining
{
  public enum BlockCode { ORPHAN, DUPLICATE, INVALID, EXPIRED };


  public partial class Blockchain
  {
    Network Network;
    BlockchainController Controller;
    
    ChainBlock BlockGenesis;
    CheckpointManager Checkpoints;
    IBlockPayloadParser BlockPayloadParser;

    ChainSocket SocketMain;
    HeaderLocator Locator;


    public Blockchain(Network network, ChainBlock genesisBlock, List<BlockLocation> checkpoints, IBlockPayloadParser blockPayloadParser)
    {
      Network = network;
      Controller = new BlockchainController(network, this);

      BlockGenesis = genesisBlock;
      Checkpoints = new CheckpointManager(checkpoints);
      BlockPayloadParser = blockPayloadParser;

      SocketMain = new ChainSocket(
        blockchain: this,
        block: genesisBlock,
        hash: GetHashBlock(genesisBlock),
        accumulatedDifficultyPrevious: 0,
        height: 0);

      Locator = new HeaderLocator(this, SocketMain.HeaderProbe);
    }
    
    public async Task startAsync()
    {
      await Controller.StartAsync();
    }

    static UInt256 GetHashBlock(ChainBlock block)
    {
      if (block.BlocksNext.Any())
      {
        return block.BlocksNext[0].Header.HashPrevious;
      }

      return new UInt256(Hashing.SHA256d(block.Header.getBytes()));
    }

    public List<BlockLocation> GetHeaderLocator() => Locator.BlockLocations;

    public ChainBlock GetBlock(UInt256 hash)
    {
      ChainSocket.SocketProbeHeader socketProbe = GetProbeAtBlock(hash);
           
      return socketProbe == null ? null : socketProbe.Block;
    }

    ChainSocket.SocketProbeHeader GetProbeAtBlock(UInt256 hash)
    {
      ChainSocket socket = SocketMain;
      ChainSocket.SocketProbeHeader probe = null;

      while (true)
      {
        if(socket == null)
        {
          return null;
        }

        probe = socket.GetProbeAtBlock(hash);

        if (probe != null)
        {
          return probe;
        }

        socket = socket.WeakerSocket;
      }
    }

    public void InsertHeader(NetworkHeader header, UInt256 headerHash)
    {
      ValidateHeader(header, headerHash, out ChainSocket.SocketProbeHeader socketProbeAtHeaderPrevious);

      ChainSocket socket = socketProbeAtHeaderPrevious.InsertHeader(header, headerHash);

      if (socket == SocketMain)
      {
        Locator.Update(socket.HeightBlockTip, socket.HashBlockTip);
        return;
      }

      InsertSocket(socket);
    }
    void ValidateHeader(NetworkHeader header, UInt256 headerHash, out ChainSocket.SocketProbeHeader socketProbe)
    {
      if (headerHash.isGreaterThan(UInt256.ParseFromCompact(header.NBits)))
      {
        throw new BlockchainException(BlockCode.INVALID);
      }

      if (IsTimestampExpired(header.UnixTimeSeconds))
      {
        throw new BlockchainException(BlockCode.EXPIRED);
      }

      socketProbe = GetProbeAtBlock(header.HashPrevious);

      if (socketProbe == null)
      {
        throw new BlockchainException(BlockCode.ORPHAN);
      }
    }

    public bool InsertBlock(NetworkBlock networkBlock, UInt256 headerHash)
    {
      IBlockPayload payload = BlockPayloadParser.Parse(networkBlock.Payload);
      ChainSocket socket = SocketMain;

      while (true)
      {
        if(socket == null)
        {
          return false;
        }

        if(socket.InsertBlockPayload(payload, headerHash))
        {
          return true;
        }

        socket = socket.WeakerSocket;
      }
    }

    bool IsTimestampExpired(ulong unixTimeSeconds)
    {
      const long MAX_FUTURE_TIME_SECONDS = 2 * 60 * 60;
      return (long)unixTimeSeconds > (DateTimeOffset.UtcNow.ToUnixTimeSeconds() + MAX_FUTURE_TIME_SECONDS);
    }

    public uint GetHeight() => SocketMain.HeightBlockTip;

    static ChainBlock GetBlockPrevious(ChainBlock block, uint depth)
    {
      if (depth == 0 || block.BlockPrevious == null)
      {
        return block;
      }

      return GetBlockPrevious(block.BlockPrevious, --depth);
    }
    
    void InsertSocket(ChainSocket newSocket)
    {
      if (newSocket.IsStrongerThan(SocketMain))
      {
        newSocket.ConnectWeakerSocket(SocketMain);
        SocketMain = newSocket;

        Locator.Create(SocketMain.HeaderProbe);
        return;
      }

      ChainSocket socket = SocketMain;
      while (!newSocket.IsStrongerThan(socket.WeakerSocket))
      {
        socket = socket.WeakerSocket;
      }

      socket.ConnectWeakerSocket(newSocket);
    }
  }
}
