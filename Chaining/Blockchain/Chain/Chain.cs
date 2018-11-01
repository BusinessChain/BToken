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
      ChainSocket Socket;
      public ChainProbe Probe;

      BlockLocator Locator;



      public Chain(ChainBlock genesisBlock)
      {
        UInt256 blockGenesisHash = new UInt256(Hashing.SHA256d(genesisBlock.Header.GetBytes()));

        Socket = new ChainSocket(
          blockGenesis: genesisBlock,
          blockGenesisHash: blockGenesisHash,
          chain: this);

        Probe = new ChainProbe(this);

        Locator = new BlockLocator(0, blockGenesisHash);

      }

      Chain(
        ChainBlock blockTip,
        UInt256 blockTipHash,
        uint blockTipHeight,
        ChainBlock blockGenesis,
        ChainBlock blockHighestAssigned,
        double accumulatedDifficultyPrevious,
        BlockLocator blockLocator)
      {
        Socket = new ChainSocket(
          blockTip: blockTip,
          blockTipHash: blockTipHash,
          blockTipHeight: blockTipHeight,
          blockGenesis: blockGenesis,
          blockHighestAssigned: blockHighestAssigned,
          accumulatedDifficultyPrevious: accumulatedDifficultyPrevious,
          chain: this);
      }


      public void InsertBlock(ChainBlock block, UInt256 headerHash)
      {
        ValidateHeader(block.Header, headerHash);

        ConnectChainBlock(block);

        if (Probe.IsTip())
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
      bool IsBlockConnectedToNextBlock(UInt256 hash) => Probe.Block.BlocksNext.Any(b => GetHeaderHash(b).IsEqual(hash));
      uint GetMedianTimePast()
      {
        const int MEDIAN_TIME_PAST = 11;

        List<uint> timestampsPast = new List<uint>();
        ChainBlock block = Probe.Block;

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
        block.BlockPrevious = Probe.Block;
        Probe.Block.BlocksNext.Add(block);
      }
      void ForkChain(ChainBlock block, UInt256 headerHash)
      {
        ChainBlock blockHighestAssigned = block.BlockStore != null ? block : null;
        uint blockTipHeight = Probe.GetHeight() + 1;

        Chain newChain = new Chain(
          blockTip: block,
          blockTipHash: headerHash,
          blockTipHeight: blockTipHeight,
          blockGenesis: block,
          blockHighestAssigned: blockHighestAssigned,
          accumulatedDifficultyPrevious: Probe.AccumulatedDifficulty,
          blockLocator: new BlockLocator(blockTipHeight, headerHash));

        Chain strongestChain = Socket.GetStrongestSocket().Chain;
        strongestChain.InsertChain(newChain);
      }
      void ExtendChain(ChainBlock block, UInt256 headerHash)
      {
        Socket.BlockTip = block;
        Socket.BlockTipHash = headerHash;
        Socket.BlockTipHeight++;
        Socket.AccumulatedDifficulty += TargetManager.GetDifficulty(block.Header.NBits);

        UpdateLocator();

        if (block.BlockStore != null && Probe.Block.BlockStore != null)
        {
          Socket.BlockHighestAssigned = block;
        }

        if (!Socket.IsStrongest())
        {
          Chain strongestChain = Socket.GetStrongestSocket().Chain;
          Socket.Disconnect();
          strongestChain.InsertChain(this);
        }
      }

      void InsertChain(Chain chain)
      {
        if (chain.IsStrongerThan(this))
        {
          ReorganizeChain(chain);
          Socket.ConnectAsSocketWeaker(chain.Socket);
        }
        else
        {
          Socket.InsertSocketRecursive(chain.Socket);
        }
      }
      void ReorganizeChain(Chain chain)
      {
        ChainSocket socketTemp = chain.Socket;
        socketTemp.Chain = this;

        chain.Socket = Socket;
        chain.Socket.Chain = chain;
        chain.Probe.Initialize();

        Socket = socketTemp;
        Probe.Initialize();
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

        Probe.Block = Socket.BlockHighestAssigned.BlocksNext[0];
        
        var blocksUnassignedPayload = new List<ChainBlock>();
        while (blocksUnassignedPayload.Count < batchSize)
        {
          Socket.BlockHighestAssigned = Probe.Block;

          if (Probe.Block.BlockStore == null)
          {
            blocksUnassignedPayload.Add(Probe.Block);
          }

          if (Probe.IsTip())
          {
            return blocksUnassignedPayload;
          }

          Probe.Block = Probe.Block.BlocksNext[0];
        }

        return blocksUnassignedPayload;
      }
      
      public uint GetHeight() => Socket.BlockTipHeight;
      public bool IsStrongerThan(Chain chain) => chain == null ? true : Socket.AccumulatedDifficulty > chain.Socket.AccumulatedDifficulty;
    }
  }
}
