using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace BToken.Chaining
{
  abstract partial class Chain
  {
    protected partial class ChainSocket
    {
      public class SocketProbe
      {
        ChainSocket Socket;

        public ChainLink ChainLink;
        public uint Depth;
        double AccumulatedDifficulty;

        public SocketProbe(ChainSocket socket)
        {
          Socket = socket;
          ChainLink = socket.ChainLink;
          AccumulatedDifficulty = socket.AccumulatedDifficulty;
        }


        public void InsertChainLink(ChainLink chainLinkNew)
        {
          Socket.Validate(chainLinkNew);

          ConnectChainLinks(ChainLink, chainLinkNew);

          if(Depth == 0)
          {
            Socket.appendChainLink(chainLinkNew);
            reset();
          }
          else
          {
            CreateFork(chainLinkNew);
          }
        }
        void CreateFork(ChainLink chainLink)
        {
          double accumulatedDifficulty = AccumulatedDifficulty + Socket.Chain.GetDifficulty(chainLink);
          uint height = GetHeight() + 1;

          var socket = new ChainSocket(
            Socket.Chain,
            chainLink,
            accumulatedDifficulty,
            height);
        }
        protected virtual void ConnectChainLinks(ChainLink chainLinkPrevious, ChainLink chainLink)
        {
          chainLink.ChainLinkPrevious = chainLinkPrevious;
          chainLinkPrevious.NextChainLinks.Add(chainLink);
        }

        uint GetHeight()
        {
          return Socket.Height - Depth;
        }
        public UInt256 getHash()
        {
          return ChainLink.Hash;
        }
        public bool IsHash(UInt256 hash)
        {
          return getHash().isEqual(hash);
        }
        public bool IsGenesis()
        {
          return ChainLink == Socket.ChainLinkGenesis;
        }
        public bool IsChainLink(ChainLink chainLink)
        {
          return ChainLink == chainLink;
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
          ChainLink = ChainLink.getChainLinkPrevious();
          AccumulatedDifficulty -= Socket.Chain.GetDifficulty(ChainLink);
          Depth++;
        }

        public void reset()
        {
          ChainLink = Socket.ChainLink;
          Depth = 0;
        }
      }
    }
  }
}
