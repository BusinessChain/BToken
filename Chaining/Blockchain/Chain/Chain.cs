using System;
using System.Collections.Generic;
using System.Linq;

using BToken.Networking;


namespace BToken.Chaining
{
  partial class Blockchain
  {
    partial class Chain
    {
      Blockchain Blockchain;
      ChainSocket Socket;
      
      BlockLocator Locator;

      public ChainBlock Block;
      UInt256 Hash;
      public double AccumulatedDifficulty { get; private set; }
      public uint Depth;



      public Chain(Blockchain blockchain, ChainBlock genesisBlock)
      {
        Blockchain = blockchain;

        UInt256 blockGenesisHash = new UInt256(Hashing.SHA256d(genesisBlock.Header.GetBytes()));

        Socket = new ChainSocket(
          blockGenesis: genesisBlock,
          blockGenesisHash: blockGenesisHash,
          chain: this);

        Locator = new BlockLocator(0, blockGenesisHash);

        Initialize();
      }

      Chain(
        Blockchain blockchain,
        ChainBlock blockTip,
        UInt256 blockTipHash,
        uint blockTipHeight,
        ChainBlock blockGenesis,
        ChainBlock blockHighestAssigned,
        double accumulatedDifficultyPrevious,
        BlockLocator blockLocator)
      {
        Blockchain = blockchain;

        Socket = new ChainSocket(
          blockTip: blockTip,
          blockTipHash: blockTipHash,
          blockTipHeight: blockTipHeight,
          blockGenesis: blockGenesis,
          blockHighestAssigned: blockHighestAssigned,
          accumulatedDifficultyPrevious: accumulatedDifficultyPrevious,
          chain: this);

        Initialize();
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
      bool IsBlockConnectedToNextBlock(UInt256 hash) => Block.BlocksNext.Any(b => GetHeaderHash(b).IsEqual(hash));
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
        ChainBlock blockHighestAssigned = block.BlockStore != null ? block : null;
        uint blockTipHeight = GetHeight() + 1;

        Chain newChain = new Chain(
          blockchain: Blockchain,
          blockTip: block,
          blockTipHash: headerHash,
          blockTipHeight: blockTipHeight,
          blockGenesis: block,
          blockHighestAssigned: blockHighestAssigned,
          accumulatedDifficultyPrevious: AccumulatedDifficulty,
          blockLocator: new BlockLocator(blockTipHeight, headerHash));

        Blockchain.InsertChain(newChain);
      }
      void ExtendChain(ChainBlock block, UInt256 headerHash)
      {
        Socket.BlockTip = block;
        Socket.BlockTipHash = headerHash;
        Socket.BlockTipHeight++;
        Socket.AccumulatedDifficulty += TargetManager.GetDifficulty(block.Header.NBits);

        UpdateLocator();

        if (block.BlockStore != null && Block.BlockStore != null)
        {
          Socket.BlockHighestAssigned = block;
        }

        if (this != Blockchain.MainChain)
        {
          Socket.Disconnect();
          Blockchain.InsertChain(this);
        }
      }

      public void ConnectAsWeakerChain(Chain chain)
      {
        Socket.ConnectAsSocketWeaker(chain.Socket);
      }
      public void InsertChainRecursive(Chain chain)
      {
        Socket.InsertSocketRecursive(chain.Socket);
      }

      public List<BlockLocation> GetBlockLocations() => Locator.BlockLocations;
      void UpdateLocator() => Locator.Update(Socket.BlockTipHeight, Socket.BlockTipHash);

      public Chain GetChainWeaker()
      {
        if(Socket.SocketWeaker == null)
        {
          return null;
        }

        return Socket.SocketWeaker.Chain;
      }

      UInt256 GetHeaderHash(ChainBlock block)
      {
        if (block == Socket.BlockTip)
        {
          return Socket.BlockTipHash;
        }

        return block.BlocksNext[0].Header.HashPrevious;
      }
      public List<ChainBlock> GetBlocksUnassignedPayload(int batchSize)
      {
        if (Socket.BlockHighestAssigned == Socket.BlockTip) { return new List<ChainBlock>(); }

        Block = Socket.BlockHighestAssigned.BlocksNext[0];
        
        var blocksUnassignedPayload = new List<ChainBlock>();
        while (blocksUnassignedPayload.Count < batchSize)
        {
          Socket.BlockHighestAssigned = Block;

          if (Block.BlockStore == null)
          {
            blocksUnassignedPayload.Add(Block);
          }

          if (IsTip())
          {
            return blocksUnassignedPayload;
          }

          Block = Block.BlocksNext[0];
        }

        return blocksUnassignedPayload;
      }
      
      public uint GetHeightTip() => Socket.BlockTipHeight;
      public uint GetHeight() => GetHeightTip() - Depth;
      public bool IsHash(UInt256 hash) => Hash.IsEqual(hash);
      public bool IsGenesis() => Block == Socket.BlockGenesis;
      public bool IsTip() => Block == Socket.BlockTip;
      public bool IsStrongerThan(Chain chain) => chain == null ? false : Socket.AccumulatedDifficulty > chain.Socket.AccumulatedDifficulty;
      public BlockLocation GetBlockLocation() => new BlockLocation(GetHeight(), Hash);
    }
  }
}
