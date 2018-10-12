using System.Diagnostics;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;

using BToken.Networking;

namespace BToken.Chaining
{
  public partial class Blockchain
  {
    public partial class SocketProbe
    {
      partial class ChainSocket
      {
        public ChainBlock BlockTip { get; private set; }
        public UInt256 BlockTipHash { get; private set; }
        public uint BlockTipHeight { get; private set; }
        public double AccumulatedDifficulty { get; private set; }

        public ChainBlock BlockGenesis { get; private set; }
        public ChainBlock BlockUnassignedPayloadDeepest { get; private set; }

        public SocketProbe Probe { get; private set; }
        BlockLocator Locator;

        ChainSocket SocketStronger;
        public ChainSocket SocketWeaker { get; private set; }


        public ChainSocket(
          ChainBlock blockGenesis,
          UInt256 blockGenesisHash,
          SocketProbe probe)
          : this(
             blockTip: blockGenesis,
             blockTipHash: blockGenesisHash,
             blockTipHeight: 0,
             blockGenesis: blockGenesis,
             blockUnassignedPayloadDeepest: null,
             accumulatedDifficultyPrevious: 0,
             blockLocator: new BlockLocator(0, blockGenesisHash),
             probe: probe)
        { }

        public ChainSocket(
          ChainBlock blockTip,
          UInt256 blockTipHash,
          uint blockTipHeight,
          ChainBlock blockGenesis,
          ChainBlock blockUnassignedPayloadDeepest,
          double accumulatedDifficultyPrevious,
          BlockLocator blockLocator,
          SocketProbe probe)
        {
          BlockTip = blockTip;
          BlockTipHash = blockTipHash;
          BlockTipHeight = blockTipHeight;
          BlockGenesis = blockGenesis;
          BlockUnassignedPayloadDeepest = blockUnassignedPayloadDeepest;
          AccumulatedDifficulty = accumulatedDifficultyPrevious + TargetManager.GetDifficulty(blockTip.Header.NBits);
          Locator = blockLocator;

          Probe = probe;
        }

        public List<ChainBlock> GetBlocksUnassignedPayload(int batchSize)
        {
          if (AllPayloadsAssigned()) { return new List<ChainBlock>(); }

          ChainBlock block = BlockUnassignedPayloadDeepest;

          var locatorBatchBlocksUnassignedPayload = new List<ChainBlock>();
          while (locatorBatchBlocksUnassignedPayload.Count < batchSize)
          {
            if (block.BlockStore == null)
            {
              locatorBatchBlocksUnassignedPayload.Add(block);
            }

            if (block == BlockTip)
            {
              return locatorBatchBlocksUnassignedPayload;
            }

            block = block.BlocksNext[0];

            if (locatorBatchBlocksUnassignedPayload.Count == 0)
            {
              BlockUnassignedPayloadDeepest = block;
            }
          }

          return locatorBatchBlocksUnassignedPayload;
        }
        public bool AllPayloadsAssigned() => BlockUnassignedPayloadDeepest == null;

        public bool LocateProbeAtBlock(UInt256 hash)
        {
          return Probe.GetAtBlock(hash);
        }

        public void InsertSocketRecursive(ChainSocket socket)
        {
          if (socket.IsStrongerThan(SocketWeaker))
          {
            ConnectAsSocketWeaker(socket);
          }
          else
          {
            SocketWeaker.InsertSocketRecursive(socket);
          }
        }
        public void ConnectAsSocketWeaker(ChainSocket socket)
        {
          if (socket != null)
          {
            socket.SocketWeaker = SocketWeaker;
            socket.SocketStronger = this;
          }

          if (SocketWeaker != null)
          {
            SocketWeaker.SocketStronger = socket;
          }

          SocketWeaker = socket;
        }

        void Disconnect()
        {
          if (SocketStronger != null)
          {
            SocketStronger.SocketWeaker = SocketWeaker;
          }
          if (SocketWeaker != null)
          {
            SocketWeaker.SocketStronger = SocketStronger;
          }
        }

        public bool IsStrongerThan(ChainSocket socket)
        {
          if (socket == null)
          {
            return true;
          }
          return AccumulatedDifficulty > socket.AccumulatedDifficulty;
        }

        UInt256 GetHeaderHash(ChainBlock block)
        {
          if (block == BlockTip)
          {
            return BlockTipHash;
          }

          return block.BlocksNext[0].Header.HashPrevious;
        }

        public List<BlockLocation> GetBlockLocations() => Locator.BlockLocations;

      }
    }
  }
}
