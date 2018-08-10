using System.Collections.Generic;
using System;

namespace BToken.Chaining
{
  public enum ChainLinkCode { ORPHAN, DUPLICATE, INVALID, EXPIRED };

  abstract partial class Chain
  {

    ChainSocket SocketMain;


    public Chain(ChainLink chainLinkGenesis)
    {
      SocketMain = new ChainSocket(chainLinkGenesis);
    }

    public void insertChainLink(ChainLink chainLink)
    {
      ChainLink chainLinkPrevious = getChainLinkPrevious(chainLink);

      chainLink.connectToPrevious(chainLinkPrevious);
      validate(chainLink);
      chainLinkPrevious.connectToNext(chainLink);

      plugChainLinkIntoSocket(chainLink);
    }
    void validate(ChainLink chainLink)
    {
      if (chainLink.getChainLinkPrevious().isConnectedToNext(chainLink))
      {
        throw new ChainLinkException(chainLink, ChainLinkCode.DUPLICATE);
      }

      chainLink.validate();
    }
    ChainLink getChainLinkPrevious(ChainLink chainLink)
    {
      ChainLink chainLinkPrevious = GetChainLink(chainLink.HashPrevious);

      if (chainLinkPrevious == null)
      {
        throw new ChainLinkException(chainLink, ChainLinkCode.ORPHAN);
      }

      return chainLinkPrevious;
    }
    void insertSocket(ChainSocket newSocket)
    {
      if (newSocket.isStrongerThan(SocketMain))
      {
        swapChain(newSocket, SocketMain);
        insertSocket(newSocket);
      }

      ChainSocket socket = SocketMain;
      while (!newSocket.isStrongerThan(socket.WeakerSocket))
      {
        socket = socket.WeakerSocket;
      }

      socket.connectWeakerSocket(newSocket);
    }
    void swapChain(ChainSocket socket1, ChainSocket socket2)
    {
      ChainLink chainLinkTemp = socket2.ChainLink;
      socket2.plugin(socket1.ChainLink);
      socket1.plugin(chainLinkTemp);
    }

    public uint getHeight()
    {
      return SocketMain.ChainLink.Height;
    }
    public UInt256 getHash()
    {
      return SocketMain.ChainLink.Hash;
    }

    protected ChainLink GetChainLink(UInt256 hash)
    {
      ChainSocket socket = SocketMain;
      resetProbes();

      while (socket != null)
      {
        if (socket.Probe.isHash(hash))
        {
          return socket.Probe.ChainLink;
        }

        if (socket.isProbeAtGenesis())
        {
          socket.bypass();

          if (socket.isWeakerSocketProbeStrongerThan(socket.StrongerSocketActive))
          {
            socket = socket.WeakerSocketActive;
          }
          else
          {
            socket = socket.StrongerSocket;
          }
          continue;
        }

        if (socket.isProbeStrongerThan(socket.StrongerSocket))
        {
          if (socket.isWeakerSocketProbeStronger())
          {
            socket = socket.WeakerSocketActive;
          }

          socket.Probe.push();
        }
        else
        {
          socket = socket.StrongerSocket;
        }

      }

      return null;
    }
    void resetProbes()
    {
      ChainSocket socket = SocketMain;

      while (socket != null)
      {
        socket.reset();
        socket = socket.WeakerSocket;
      }
    }

    void plugChainLinkIntoSocket(ChainLink chainLink)
    {
      ChainSocket socket = getSocket(chainLink.getChainLinkPrevious());

      if(socket == null)
      {
        socket = new ChainSocket(chainLink);
        return;
      }

      socket.plugin(chainLink);

      if (socket != SocketMain)
      {
        socket.remove();
        insertSocket(socket);
      }
    }
    ChainSocket getSocket(ChainLink chainLink)
    {
      ChainSocket socket = SocketMain;

      while (socket != null)
      {
        if(socket.ChainLink == chainLink)
        {
          return socket;
        }
        socket = socket.WeakerSocket;
      }

      return null;
    }
    
    
    /// <summary>
    /// Generates the chain locator object. If the checkpoint is not null, it will be treated as if the Genesis block hash.
    /// </summary>
    protected List<UInt256> getChainLinkLocator(UInt256 checkpointHash, Func<uint,uint> getNextLocation)
    {
      List<UInt256> chainLinkLocator = new List<UInt256>();
      SocketMain.Probe.reset();
      uint locator = 0;

      while (true)
      {
        if(SocketMain.Probe.isHash(checkpointHash) || SocketMain.isProbeAtGenesis())
        {
          chainLinkLocator.Add(SocketMain.Probe.getHash());
          return chainLinkLocator;
        }

        if (locator == SocketMain.Probe.Depth)
        {
          chainLinkLocator.Add(SocketMain.Probe.getHash());
          locator = getNextLocation(SocketMain.Probe.Depth);
        }

        SocketMain.Probe.push();
      }

    }
  }
}
