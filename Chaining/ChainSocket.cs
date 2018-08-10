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
      public ChainSocket StrongerSocket;
      public ChainSocket StrongerSocketActive;
      public ChainSocket WeakerSocket;
      public ChainSocket WeakerSocketActive;

      public ChainLink ChainLink;
      public SocketProbe Probe;

      readonly ChainLink ChainLinkGenesis;

      public ChainSocket(ChainLink chainLinkGenesis)
      {
        ChainLinkGenesis = chainLinkGenesis;
        ChainLink = chainLinkGenesis;

        Probe = new SocketProbe(this);
      }

      public bool isWeakerSocketProbeStrongerThan(ChainSocket socket)
      {
        return WeakerSocketActive != null && WeakerSocketActive.isProbeStrongerThan(socket);
      }
      public bool isWeakerSocketProbeStronger()
      {
        return WeakerSocketActive != null && WeakerSocketActive.isProbeStrongerThan(this);
      }
      public void bypass()
      {
        if (StrongerSocketActive != null)
        {
          StrongerSocketActive.WeakerSocketActive = WeakerSocketActive;
        }
        if (WeakerSocketActive != null)
        {
          WeakerSocketActive.StrongerSocketActive = StrongerSocketActive;
        }
      }
      public void reset()
      {
        Probe.reset();

        StrongerSocketActive = StrongerSocket;
        WeakerSocketActive = WeakerSocket;
      }

      public void connectWeakerSocket(ChainSocket weakerSocket)
      {
        weakerSocket.WeakerSocket = WeakerSocket;
        weakerSocket.WeakerSocketActive = WeakerSocket;

        weakerSocket.StrongerSocket = this;
        weakerSocket.StrongerSocketActive = this;

        if (WeakerSocket != null)
        {
          WeakerSocket.StrongerSocket = weakerSocket;
          WeakerSocket.StrongerSocketActive = weakerSocket;
        }

        WeakerSocket = weakerSocket;
        WeakerSocketActive = weakerSocket;
      }

      public void remove()
      {
        if(StrongerSocket != null)
        {
          StrongerSocket.WeakerSocket = WeakerSocket;
          StrongerSocket.WeakerSocketActive = WeakerSocket;
        }
        if (WeakerSocket != null)
        {
          WeakerSocket.StrongerSocket = StrongerSocket;
          WeakerSocket.StrongerSocketActive = StrongerSocket;
        }

        StrongerSocket = null;
        StrongerSocketActive = null;
        WeakerSocket = null;
        WeakerSocketActive = null;
      }

      public void plugin(ChainLink chainLink)
      {
        ChainLink = chainLink;
        Probe.reset();
      }

      public bool isStrongerThan(ChainSocket socket)
      {
        if (socket == null)
        {
          return true;
        }
        return ChainLink.isStrongerThan(socket.ChainLink);
      }
      public bool isProbeStrongerThan(ChainSocket socket)
      {
        if (socket == null)
        {
          return true;
        }
        return Probe.isStrongerThan(socket.Probe);
      }

      public bool isProbeAtGenesis()
      {
        return ChainLinkGenesis == Probe.ChainLink;
      }
    }
  }
}
