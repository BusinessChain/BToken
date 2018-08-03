using System.Collections.Generic;
using System;

namespace BToken.Chaining
{
  public enum ChainLinkCode { ORPHAN, DUPLICATE, INVALID, EXPIRED };

  abstract partial class Chain
  {

    ChainSocket Socket;


    public Chain(ChainLink chainLinkGenesis)
    {
      Socket = new ChainSocket(chainLinkGenesis);
    }

    public void insertChainLink(ChainLink chainLink)
    {
      ChainLink chainLinkPrevious = getChainLinkPrevious(chainLink);

      chainLink.connectToPrevious(chainLinkPrevious);
      validate(chainLink);
      chainLinkPrevious.connectToNext(chainLink);

      ChainSocket socket = plugChainLinkIntoSocket(chainLink);
      insertSocket(socket);
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

    public uint getHeight()
    {
      return Socket.ChainLink.Height;
    }
    public UInt256 getHash()
    {
      return Socket.ChainLink.Hash;
    }

    protected bool ContainsChainLinkHash(UInt256 hash)
    {
      return GetChainLink(hash) != null;
    }
    protected ChainLink GetChainLink(UInt256 hash)
    {
      ChainSocket socket = Socket;
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
      ChainSocket socket = Socket;

      while (socket != null)
      {
        socket.reset();
        socket = socket.WeakerSocket;
      }
    }

    ChainSocket plugChainLinkIntoSocket(ChainLink chainLink)
    {
      ChainSocket socket = getSocket(chainLink.getChainLinkPrevious());

      if (socket != null)
      {
        socket.plugin(chainLink);
        removeSocket(socket);
      }
      else
      {
        socket = new ChainSocket(chainLink);
      }

      return socket;
    }
    ChainSocket getSocket(ChainLink chainLink)
    {
      ChainSocket socket = Socket;

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
    void insertSocket(ChainSocket newSocket)
    {
      if (newSocket.isStrongerThan(Socket))
      {
        swapChain(newSocket, Socket);
        insertSocket(newSocket);
      }

      ChainSocket socket = Socket;
      while (!newSocket.isStrongerThan(socket.WeakerSocket))
      {
        socket = socket.WeakerSocket;
      }

      socket.insertAsWeakerSocket(newSocket);
    }
    void swapChain(ChainSocket socket1, ChainSocket socket2)
    {
      ChainLink chainLinkTemp = socket2.ChainLink;
      socket2.plugin(socket1.ChainLink);
      socket1.plugin(chainLinkTemp);
    }
    
    void removeSocket(ChainSocket socket)
    {
      socket.StrongerSocket.insertAsWeakerSocket(socket.WeakerSocket);
      socket.disconnect();
    }


    protected List<UInt256> getChainLinkLocator(Func<uint,uint> getNextLocation)
    {
      List<UInt256> chainLinklocator = new List<UInt256>();
      Socket.Probe.reset();
      uint locator = 0;

      while (!Socket.isProbeAtGenesis())
      {
        if (locator == Socket.Probe.Depth)
        {
          chainLinklocator.Add(Socket.Probe.getHash());
          locator = getNextLocation(Socket.Probe.Depth);
        }
        Socket.Probe.push();
      }

      chainLinklocator.Add(Socket.Probe.getHash()); // This must be genesis

      return chainLinklocator;
    }
  }
}
