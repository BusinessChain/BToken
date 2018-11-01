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
    partial class Chain
    {
      public class ChainSocket
      {
        public ChainBlock BlockTip;
        public UInt256 BlockTipHash;
        public uint BlockTipHeight;
        public double AccumulatedDifficulty;

        public ChainBlock BlockGenesis;
        public ChainBlock BlockHighestAssigned;

        public Chain Chain;

        public ChainSocket SocketStronger;
        public ChainSocket SocketWeaker;


        public ChainSocket(
          ChainBlock blockGenesis,
          UInt256 blockGenesisHash,
          Chain chain)
          : this(
             blockTip: blockGenesis,
             blockTipHash: blockGenesisHash,
             blockTipHeight: 0,
             blockGenesis: blockGenesis,
             blockHighestAssigned: blockGenesis,
             accumulatedDifficultyPrevious: 0,
             chain: chain)
        { }

        public ChainSocket(
          ChainBlock blockTip,
          UInt256 blockTipHash,
          uint blockTipHeight,
          ChainBlock blockGenesis,
          ChainBlock blockHighestAssigned,
          double accumulatedDifficultyPrevious,
          Chain chain)
        {
          BlockTip = blockTip;
          BlockTipHash = blockTipHash;
          BlockTipHeight = blockTipHeight;
          BlockGenesis = blockGenesis;
          BlockHighestAssigned = blockHighestAssigned;
          AccumulatedDifficulty = accumulatedDifficultyPrevious + TargetManager.GetDifficulty(blockTip.Header.NBits);

          Chain = chain;
        }

        public ChainSocket GetStrongestSocket()
        {
          if (IsStrongest())
          {
            return this;
          }
          else
          {
            return SocketStronger.GetStrongestSocket();
          }
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

        public void ExtendChain(ChainBlock block, UInt256 headerHash)
        {
          BlockTip = block;
          BlockTipHash = headerHash;
          BlockTipHeight++;
          AccumulatedDifficulty += TargetManager.GetDifficulty(block.Header.NBits);
        }

        public bool IsStrongerThan(ChainSocket socket)
        {
          if (socket == null)
          {
            return true;
          }
          return AccumulatedDifficulty > socket.AccumulatedDifficulty;
        }
        public bool IsStrongest()
        {
          return SocketStronger == null;
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
        public void Disconnect()
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
      }
    }
  }
}
