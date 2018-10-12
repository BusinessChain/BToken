using System;
using System.Collections.Generic;
using System.Linq;

using BToken.Networking;


namespace BToken.Chaining
{
  partial class Blockchain
  {
    public partial class SocketProbe
    {
      Blockchain Blockchain;
      ChainSocket Socket;

      public ChainBlock Block;
      UInt256 Hash;

      public uint Depth;
      public double AccumulatedDifficulty { get; private set; }


      public SocketProbe(Blockchain blockchain, ChainBlock genesisBlock)
      {
        Blockchain = blockchain;

        Socket = new ChainSocket(
          blockchain: blockchain,
          blockGenesis: genesisBlock,
          blockGenesisHash: new UInt256(Hashing.SHA256d(genesisBlock.Header.getBytes())),
          probe: this);

        Initialize();
      }

      SocketProbe(
        Blockchain blockchain,
        ChainBlock blockTip,
        UInt256 blockTipHash,
        uint blockTipHeight,
        ChainBlock genesisBlock, 
        UInt256 hash, 
        uint depth, 
        double accumulatedDifficulty)
      {
        Blockchain = blockchain;

        Socket = new ChainSocket(
          blockTip: blockTip,
          blockTipHash: blockTipHash,
          blockTipHeight: blockTipHeight,
          blockUnassignedPayloadDeepest: null,
          blockGenesis: genesisBlock,
          blockGenesisHash: new UInt256(Hashing.SHA256d(genesisBlock.Header.getBytes())),
          probe: this);

        Block = genesisBlock;
        Hash = hash;
        AccumulatedDifficulty = accumulatedDifficulty;
        Depth = depth;
      }

      public void Initialize()
      {
        Block = Socket.BlockTip;
        Hash = Socket.BlockTipHash;
        AccumulatedDifficulty = Socket.AccumulatedDifficulty;

        Depth = 0;
      }

      public bool GetAtBlock(UInt256 hash)
      {
        Initialize();

        while (true)
        {
          if (IsHash(hash))
          {
            return true;
          }

          if (IsGenesis())
          {
            return false;
          }

          Push();
        }
      }

      public void Push()
      {
        Hash = Block.Header.HashPrevious;
        Block = Block.BlockPrevious;
        AccumulatedDifficulty -= TargetManager.GetDifficulty(Block.Header.NBits);

        Depth++;
      }

      public void InsertBlock(ChainBlock block, UInt256 headerHash)
      {
        ValidateHeader(block.Header, headerHash);

        ConnectChainBlock(block);

        if (IsTip())
        {
          ExtendChain(block, headerHash);
        }
        else
        {
          ForkChain(block, headerHash);
        }
      }
      void ValidateHeader(NetworkHeader header, UInt256 headerHash)
      {
        CheckProofOfWork(header, headerHash);
        CheckTimeStamp(header);

        if (IsBlockConnectedToNextBlock(headerHash))
        {
          throw new BlockchainException(BlockCode.DUPLICATE);
        }

        if (header.UnixTimeSeconds <= GetMedianTimePast())
        {
          throw new BlockchainException(BlockCode.INVALID);
        }
      }
      void CheckProofOfWork(NetworkHeader header, UInt256 headerHash)
      {
        if (headerHash.IsGreaterThan(UInt256.ParseFromCompact(header.NBits)))
        {
          throw new BlockchainException(BlockCode.INVALID);
        }

        if (header.NBits != TargetManager.GetNextTargetBits(this))
        {
          throw new BlockchainException(BlockCode.INVALID);
        }
      }
      void CheckTimeStamp(NetworkHeader header)
      {
        if (IsTimestampPremature(header.UnixTimeSeconds))
        {
          throw new BlockchainException(BlockCode.PREMATURE);
        }
      }
      bool IsTimestampPremature(ulong unixTimeSeconds)
      {
        const long MAX_FUTURE_TIME_SECONDS = 2 * 60 * 60;
        return (long)unixTimeSeconds > (DateTimeOffset.UtcNow.ToUnixTimeSeconds() + MAX_FUTURE_TIME_SECONDS);
      }
      void ConnectChainBlock(ChainBlock block)
      {
        block.BlockPrevious = Block;
        Block.BlocksNext.Add(block);
      }
      void ForkChain(ChainBlock block, UInt256 headerHash)
      {
        uint blockTipHeight = GetHeight() + 1;

        SocketProbe newSocketProbe = new SocketProbe(
          blockchain: Blockchain,

          );

        Blockchain.InsertProbe(newSocketProbe);
      }
      SocketProbe Clone()
      {
        ChainSocket socket =  new ChainSocket(
          blockchain: Blockchain,
          blockTip: block,
          blockTipHash: headerHash,
          blockTipHeight: blockTipHeight,
          blockGenesis: block,
          blockUnassignedPayloadDeepest: block,
          accumulatedDifficultyPrevious: AccumulatedDifficulty,
          blockLocator: new BlockLocator(blockTipHeight, headerHash),);

      }
      void ExtendChain(ChainBlock block, UInt256 headerHash)
      {
        ChainBlock blockGenesis = Socket.BlockGenesis;

        ChainBlock blockUnassignedPayloadDeepest = null;
        if (!AllPayloadsAssigned())
        {
          blockUnassignedPayloadDeepest = Socket.BlockUnassignedPayloadDeepest;
        }
        else
        {
          if (block.BlockStore == null)
          {
            blockUnassignedPayloadDeepest = block;
          }
        }

        uint blockTipHeight = GetHeight() + 1;

        BlockLocator locator = Socket.Locator;
        locator.Update(blockTipHeight, headerHash);

        var socketExtendChain = new ChainSocket(
          blockchain: Blockchain,
          blockTip: block,
          blockTipHash: headerHash,
          blockTipHeight: blockTipHeight,
          blockGenesis: blockGenesis,
          blockUnassignedPayloadDeepest: blockUnassignedPayloadDeepest,
          accumulatedDifficultyPrevious: AccumulatedDifficulty,
          blockLocator: locator,
          this);

        Blockchain.InsertSocket(socketExtendChain);

        Disconnect();
      }

      public void Disconnect()
      {
        Socket.Disconnect();
      }
      bool IsBlockConnectedToNextBlock(UInt256 hash) => Block.BlocksNext.Any(b => Socket.GetHeaderHash(b).IsEqual(hash));
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

      public uint GetHeightTip() => Socket.BlockTipHeight;
      public uint GetHeight() => GetHeightTip() - Depth;
      public bool IsHash(UInt256 hash) => Hash.IsEqual(hash);
      public bool IsGenesis() => Block == Socket.BlockGenesis;
      public bool IsTip() => Block == Socket.BlockTip;
      public bool AllPayloadsAssigned() => Socket.AllPayloadsAssigned();
      public bool IsStrongerThan(SocketProbe probe) => probe == null ? false : AccumulatedDifficulty > probe.AccumulatedDifficulty;
      public BlockLocation GetBlockLocation() => new BlockLocation(GetHeight(), Hash);
    }
  }
}
