using System;
using System.Collections.Generic;
using System.Linq;

namespace BToken.Chaining
{
  partial class Blockchain
  {
    partial class ChainSocket
    {
      public class SocketProbe
      {
        ChainSocket Socket;
        
        public ChainBlock Block;
        public UInt256 Hash;
        public uint Depth;
        public double AccumulatedDifficulty;

        bool DeeperThanCheckpoint;

        public SocketProbe(ChainSocket socket)
        {
          Socket = socket;

          Block = socket.Block;
          Hash = socket.Hash;
          AccumulatedDifficulty = socket.AccumulatedDifficulty;
        }
        
        public ChainBlock InsertHeader(NetworkHeader header, UInt256 headerHash)
        {
          Validate(header, headerHash);

          var block = new ChainBlock(header);
          
          ConnectBlocks(Block, block);

          return block;
        }
        void Validate(NetworkHeader header, UInt256 headerHash)
        {
          if (IsBlockConnectedToHash(headerHash))
          {
            throw new BlockchainException(BlockCode.DUPLICATE);
          }

          if (header.NBits != TargetManager.GetNextTargetBits(this))
          {
            throw new BlockchainException(BlockCode.INVALID);
          }

          if (headerHash.isGreaterThan(UInt256.ParseFromCompact(header.NBits)))
          {
            throw new BlockchainException(BlockCode.INVALID);
          }

          if (header.UnixTimeSeconds <= getMedianTimePast())
          {
            throw new BlockchainException(BlockCode.INVALID);
          }

          // This applies only if checkpoint is already in the chain and a new block wants to connect prior to it.
          if (DeeperThanCheckpoint)  
          {
            throw new BlockchainException(BlockCode.CHECKPOINT);
          }

          if (GetHeight() + 1 == Checkpoint.Height && !headerHash.isEqual(Checkpoint.Hash))
          {
            throw new BlockchainException(BlockCode.CHECKPOINT);
          }
        }
        bool IsBlockConnectedToHash(UInt256 hash)
        {
          return Block.BlocksNext.Any(b => CalculateHash(b.Header.getBytes()).isEqual(hash));
        }
        uint getMedianTimePast()
        {
          const int MEDIAN_TIME_PAST = 11;

          List<uint> timestampsPast = new List<uint>();
          ChainBlock block = Block;

          int depth = 0;
          while (depth < MEDIAN_TIME_PAST)
          {
            timestampsPast.Add(block.Header.UnixTimeSeconds);

            if (block == Socket.BlockGenesis)
            { break; }

            block = block.BlockPrevious;
            depth++;
          }

          timestampsPast.Sort();

          return timestampsPast[timestampsPast.Count / 2];
        }
        void ConnectBlocks(ChainBlock blockPrevious, ChainBlock block)
        {
          block.BlockPrevious = blockPrevious;
          blockPrevious.BlocksNext.Add(block);
        }

        public uint GetHeight()
        {
          return Socket.Height - Depth;
        }
        public bool IsHash(UInt256 hash)
        {
          return Hash.isEqual(hash);
        }
        public bool IsGenesis()
        {
          return Block == Socket.BlockGenesis;
        }
        public bool IsBlock(ChainBlock blockHeader)
        {
          return Block == blockHeader;
        }
        public bool isStrongerThan(SocketProbe probe)
        {
          if(probe == null)
          {
            return false;
          }

          return AccumulatedDifficulty > probe.AccumulatedDifficulty;
        }
        
        public void push()
        {
          if(Hash.isEqual(Checkpoint.Hash))
          {
            DeeperThanCheckpoint = true;
          }

          Hash = Block.Header.HashPrevious;
          Block = Block.BlockPrevious;
          AccumulatedDifficulty -= TargetManager.GetDifficulty(Block.Header.NBits);
          Depth++;
        }

        public void reset()
        {
          Block = Socket.Block;
          Hash = Socket.Hash;
          Depth = 0;
          DeeperThanCheckpoint = false;
        }
      }
    }
  }
}
