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
        public ChainBlock BlockTip;
        public UInt256 BlockTipHash;
        public uint BlockTipHeight;
        public double AccumulatedDifficulty;

        public ChainBlock BlockGenesis { get; private set; }
        public ChainBlock BlockUnassignedPayloadDeepest;

        public SocketProbe Probe { get; private set; }

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
             probe: probe)
        { }

        public ChainSocket(
          ChainBlock blockTip,
          UInt256 blockTipHash,
          uint blockTipHeight,
          ChainBlock blockGenesis,
          ChainBlock blockUnassignedPayloadDeepest,
          double accumulatedDifficultyPrevious,
          SocketProbe probe)
        {
          BlockTip = blockTip;
          BlockTipHash = blockTipHash;
          BlockTipHeight = blockTipHeight;
          BlockGenesis = blockGenesis;
          BlockUnassignedPayloadDeepest = blockUnassignedPayloadDeepest;
          AccumulatedDifficulty = accumulatedDifficultyPrevious + TargetManager.GetDifficulty(blockTip.Header.NBits);

          Probe = probe;
        }

        public bool AllPayloadsAssigned() => BlockUnassignedPayloadDeepest == null;
        
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

        public bool IsStrongerThan(ChainSocket socket)
        {
          if (socket == null)
          {
            return true;
          }
          return AccumulatedDifficulty > socket.AccumulatedDifficulty;
        }
        
      }
    }
  }
}
