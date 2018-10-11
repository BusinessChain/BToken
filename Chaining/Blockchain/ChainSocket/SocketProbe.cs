using System;
using System.Collections.Generic;
using System.Linq;

using BToken.Networking;


namespace BToken.Chaining
{
  partial class Blockchain
  {
    partial class ChainSocket
    {
      public class SocketProbe
      {
        public ChainSocket Socket { get; private set; }

        public ChainBlock Block;
        UInt256 Hash;

        public uint Depth;
        bool IsDeeperThanCheckpoint;
        public double AccumulatedDifficulty { get; private set; }


        public SocketProbe(ChainSocket socket)
        {
          Socket = socket;
          Reset();
        }

        public void Reset()
        {
          Block = Socket.BlockTip;
          Hash = Socket.BlockTipHash;
          AccumulatedDifficulty = Socket.AccumulatedDifficulty;

          Depth = 0;

          IsDeeperThanCheckpoint = false;
        }

        public void Push()
        {
          Hash = Block.Header.HashPrevious;
          Block = Block.BlockPrevious;
          AccumulatedDifficulty -= TargetManager.GetDifficulty(Block.Header.NBits);

          IsDeeperThanCheckpoint |= Socket.Blockchain.Checkpoints.IsCheckpoint(GetHeight());

          Depth++;
        }

        public void InsertBlock(ChainBlock block, UInt256 headerHash)
        {
          ConnectChainBlock(block);

          if (!IsTip())
          {
            ForkChain(block, headerHash);
          }
          else
          {
            ExtendChain(block, headerHash);
          }
        }
        void ConnectChainBlock(ChainBlock block)
        {
          block.BlockPrevious = Block;
          Block.BlocksNext.Add(block);
        }
        void ForkChain(ChainBlock block, UInt256 headerHash)
        {
          uint blockTipHeight = GetHeight() + 1;

          var socketForkChain = new ChainSocket(
            blockchain: Socket.Blockchain,
            blockTip: block,
            blockTipHash: headerHash,
            blockTipHeight: blockTipHeight,
            blockGenesis: block,
            blockUnassignedPayloadDeepest: block,
            accumulatedDifficultyPrevious: AccumulatedDifficulty,
            blockLocator: new BlockLocator(blockTipHeight, headerHash));

          Socket.Blockchain.InsertSocket(socketForkChain);
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
            blockchain: Socket.Blockchain,
            blockTip: block,
            blockTipHash: headerHash,
            blockTipHeight: blockTipHeight,
            blockGenesis: blockGenesis,
            blockUnassignedPayloadDeepest: blockUnassignedPayloadDeepest,
            accumulatedDifficultyPrevious: AccumulatedDifficulty,
            blockLocator: locator);

          Socket.Blockchain.InsertSocket(socketExtendChain);

          Disconnect();
        }

        public void ValidateHeader(NetworkHeader header, UInt256 headerHash)
        {
          if (IsBlockConnectedToNextBlock(headerHash))
          {
            throw new BlockchainException(BlockCode.DUPLICATE);
          }

          if (header.NBits != TargetManager.GetNextTargetBits(this))
          {
            throw new BlockchainException(BlockCode.INVALID);
          }

          if (header.UnixTimeSeconds <= GetMedianTimePast())
          {
            throw new BlockchainException(BlockCode.INVALID);
          }

          if (!Socket.Blockchain.Checkpoints.ValidateBlockLocation(GetHeight() + 1, headerHash))
          {
            throw new BlockchainException(BlockCode.INVALID);
          }

          if (IsDeeperThanCheckpoint)
          {
            throw new BlockchainException(BlockCode.INVALID);
          }          

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

        public uint GetHeight() => Socket.BlockTipHeight - Depth;
        public bool IsHash(UInt256 hash) => Hash.IsEqual(hash);
        public bool IsGenesis() => Block == Socket.BlockGenesis;
        public bool IsTip() => Block == Socket.BlockTip;
        public bool AllPayloadsAssigned() => Socket.AllPayloadsAssigned();
        public bool IsStrongerThan(SocketProbe probe) => probe == null ? false : AccumulatedDifficulty > probe.AccumulatedDifficulty;
        public BlockLocation GetBlockLocation() => new BlockLocation(GetHeight(), Hash);
      }
    }
  }
}
