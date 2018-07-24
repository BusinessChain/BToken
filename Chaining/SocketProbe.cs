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
        public uint Depth = 0;

        public SocketProbe(ChainSocket socket)
        {
          Socket = socket;
          ChainLink = socket.ChainLink;
        }

        public UInt256 getHash()
        {
          return ChainLink.Hash;
        }
        public bool isHash(UInt256 hash)
        {
          return getHash() == hash;
        }

        public bool isStrongerThan(SocketProbe probe)
        {
          return ChainLink.isStrongerThan(probe.ChainLink);
        }

        public void push()
        {
          ChainLink = ChainLink.getChainLinkPrevious();
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
