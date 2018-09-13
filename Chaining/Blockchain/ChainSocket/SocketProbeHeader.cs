﻿using System;
using System.Collections.Generic;
using System.Linq;

using BToken.Networking;


namespace BToken.Chaining
{
  partial class Blockchain
  {
    partial class ChainSocket
    {
      public class SocketProbeHeader
      {
        ChainSocket Socket;
        
        public ChainBlock Block;
        public UInt256 Hash;

        public uint Depth;
        bool IsDeeperThanCheckpoint;
        public double AccumulatedDifficulty;


        public SocketProbeHeader(ChainSocket socket)
        {
          Socket = socket;
          Reset();
        }

        public void Reset()
        {
          Block = Socket.BlockTip;
          Hash = Socket.HashBlockTip;
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
        bool IsBlockConnectedToHash(UInt256 hash) => Block.BlocksNext.Any(b => GetHashBlock(b).isEqual(hash));
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

        public uint GetHeight() => Socket.HeightBlockTip - Depth;
        public bool IsHash(UInt256 hash) => Hash.isEqual(hash);
        public bool IsGenesis() => Block == Socket.BlockGenesis;
        public bool IsPayloadAssigned() => Block.IsPayloadAssigned();
        public bool IsStrongerThan(SocketProbeHeader probe) => probe == null ? false : AccumulatedDifficulty > probe.AccumulatedDifficulty;
        public BlockLocation GetBlockLocation() => new BlockLocation(GetHeight(), Hash);
      }
    }
  }
}