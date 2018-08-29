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
        public UInt256 Hash;
        public double AccumulatedDifficulty;

        public uint Depth;

        bool IsDeeperThanCheckpoint;


        public SocketProbe(ChainSocket socket)
        {
          Socket = socket;

          reset();
        }

        public void reset()
        {
          Block = Socket.Block;
          Hash = Socket.Hash;
          AccumulatedDifficulty = Socket.AccumulatedDifficulty;

          Depth = 0;

          IsDeeperThanCheckpoint = false;
        }

        public void push()
        {
          Hash = Block.Header.HashPrevious;
          Block = Block.BlockPrevious;
          AccumulatedDifficulty -= TargetManager.GetDifficulty(Block.Header.NBits);

          IsDeeperThanCheckpoint |= Socket.Blockchain.Checkpoints.IsCheckpoint(GetHeight());

          Depth++;
        }
        
        public ChainSocket InsertHeader(NetworkHeader header, UInt256 headerHash)
        {
          ValidateHeader(header, headerHash);

          var block = new ChainBlock(header);
          
          ConnectBlocks(Block, block);

          if (Depth == 0)
          {
            Socket.ConnectNextBlock(block, headerHash);

            if(Socket.StrongerSocket != null)
            {
              Socket.Disconnect();
            }

            return Socket;
          }
          else
          {
            return new ChainSocket(
              Socket.Blockchain,
              block,
              headerHash,
              AccumulatedDifficulty,
              GetHeight() + 1);
          }
        }
        void ValidateHeader(NetworkHeader header, UInt256 headerHash)
        {
          if (IsBlockConnectedToHash(headerHash))
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
        bool IsBlockConnectedToHash(UInt256 hash)
        {
          return Block.BlocksNext.Any(b => CalculateHash(b.Header.getBytes()).isEqual(hash));
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

            if (block == Socket.Blockchain.BlockGenesis)
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
          return Block == Socket.Blockchain.BlockGenesis;
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
                
        public BlockLocation GetBlockLocation()
        {
          return new BlockLocation(GetHeight(), Hash);
        }
      }
    }
  }
}
