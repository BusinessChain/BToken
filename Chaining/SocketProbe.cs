using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

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
        public uint Depth;
        double AccumulatedDifficulty;

        bool DeeperThanCheckpoint;

        public SocketProbe(ChainSocket socket)
        {
          Socket = socket;
          Block = socket.Block;
          AccumulatedDifficulty = socket.AccumulatedDifficulty;
        }
        
        public ChainSocket InsertBlock(ChainBlock blockNew)
        {
          Validate(blockNew);

          ConnectBlocks(Block, blockNew);

          if(Depth == 0)
          {
            Socket.AppendChainHeader(blockNew);
            return Socket;
          }
          else
          {
            return CreateFork(blockNew);
          }
        }
        void Validate(ChainBlock blockNew)
        {
          if (IsHeaderConnectedToHeaderNew(blockNew))
          {
            throw new ChainLinkException(blockNew, ChainLinkCode.DUPLICATE);
          }
          
          if (blockNew.NBits != TargetManager.GetNextTargetBits(this))
          {
            throw new ChainLinkException(blockNew, ChainLinkCode.INVALID);
          }

          if (blockNew.Hash.isGreaterThan(TargetManager.GetTarget(blockNew.NBits)))
          {
            throw new ChainLinkException(blockNew, ChainLinkCode.INVALID);
          }

          if (DeeperThanCheckpoint)
          {
            throw new ChainLinkException(blockNew, ChainLinkCode.CHECKPOINT);
          }

          if (blockNew.UnixTimeSeconds <= getMedianTimePast())
          {
            throw new ChainLinkException(blockNew, ChainLinkCode.INVALID);
          }
        }
        ulong getMedianTimePast()
        {
          const int MEDIAN_TIME_PAST = 11;

          List<ulong> timestampsPast = new List<ulong>();
          ChainBlock block = Block;

          int depth = 0;
          while (depth < MEDIAN_TIME_PAST)
          {
            timestampsPast.Add(block.UnixTimeSeconds);

            if (block == BlockGenesis)
            { break; }

            block = block.BlockPrevious;
            depth++;
          }

          timestampsPast.Sort();

          return timestampsPast[timestampsPast.Count / 2];
        }
        bool IsHeaderConnectedToHeaderNew(ChainBlock blockNew)
        {
          return Block.BlocksNext.Any(c => c.Hash.isEqual(blockNew.Hash));
        }
        ChainSocket CreateFork(ChainBlock blockNew)
        {
          return new ChainSocket(
            Socket.Blockchain,
            blockNew,
            AccumulatedDifficulty,
            GetHeight());
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
        public UInt256 getHash()
        {
          return Block.Hash;
        }
        public bool IsHash(UInt256 hash)
        {
          return getHash().isEqual(hash);
        }
        public bool IsGenesis()
        {
          return Block == BlockGenesis;
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
          if(Block.Hash.isEqual(CheckpointHash))
          {
            DeeperThanCheckpoint = true;
          }

          Block = Block.BlockPrevious;
          AccumulatedDifficulty -= TargetManager.GetDifficulty(Block.NBits);
          Depth++;
        }

        public void reset()
        {
          Block = Socket.Block;
          Depth = 0;
          DeeperThanCheckpoint = false;
        }
      }
    }
  }
}
