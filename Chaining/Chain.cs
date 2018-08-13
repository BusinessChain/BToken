using System.Collections.Generic;
using System;

namespace BToken.Chaining
{
  public enum ChainLinkCode { ORPHAN, DUPLICATE, INVALID, EXPIRED, CHECKPOINT };

  abstract partial class Chain
  {

    ChainSocket SocketMain;


    public Chain(ChainLink chainLinkGenesis)
    {
      SocketMain = new ChainSocket(
        this, 
        chainLinkGenesis,
        GetDifficulty(chainLinkGenesis),
        0);
    }

    public void insertChainLink(ChainLink chainLink)
    {
      ChainSocket.SocketProbe socketProbeChainLinkPrevious = GetSocketProbe(chainLink.HashPrevious);

      if (socketProbeChainLinkPrevious == null)
      {
        throw new ChainLinkException(chainLink, ChainLinkCode.ORPHAN);
      }

      socketProbeChainLinkPrevious.InsertChainLink(chainLink);
    }
    protected virtual void ConnectChainLinks(ChainLink chainLinkPrevious, ChainLink chainLink)
    {
      chainLink.Height = chainLinkPrevious.Height + 1; //Weg, die Height soll in den Sockel
      chainLink.ChainLinkPrevious = chainLinkPrevious;
      chainLinkPrevious.NextChainLinks.Add(chainLink);
    }
    protected virtual void Validate(ChainLink chainLinkPrevious, ChainLink chainLink)
    {

      if (chainLinkPrevious.isConnectedToNext(chainLink))
      {
        throw new ChainLinkException(chainLink, ChainLinkCode.DUPLICATE);
      }
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
      socket2.appendChainLink(socket1.ChainLink);
      socket1.appendChainLink(chainLinkTemp);
    }

    public abstract double GetDifficulty(ChainLink chainLink);
    protected bool IsChainLinkDeeperThanCheckpoint(ChainLink chainLink, UInt256 checkpointHash)
    {
      ChainSocket socket = SocketMain;
      socket.Probe.reset();
      
      while(!socket.Probe.IsGenesis())
      {
        if (socket.Probe.IsChainLink(chainLink))
        {
          return false;
        }

        if (socket.Probe.IsHash(checkpointHash))
        {
          return true;
        }

        socket.Probe.push();
      }

      throw new InvalidOperationException("Neither chainLink nor checkpoint in chain encountered.");
    }
    public uint getHeight()
    {
      return SocketMain.ChainLink.Height;
    }
    public UInt256 getHash()
    {
      return SocketMain.ChainLink.Hash;
    }

    protected ChainSocket.SocketProbe GetSocketProbe(UInt256 hash)
    {
      ChainSocket socket = SocketMain;
      resetProbes();

      while (socket != null)
      {
        if (socket.Probe.IsHash(hash))
        {
          return socket.Probe;
        }

        if (socket.Probe.IsGenesis())
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
    protected ChainLink GetChainLink(UInt256 hash)
    {
      return GetSocketProbe(hash).ChainLink;
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
        if(SocketMain.Probe.IsHash(checkpointHash) || SocketMain.Probe.IsGenesis())
        {
          chainLinkLocator.Add(SocketMain.Probe.getHash());
          return chainLinkLocator;
        }

        if (locator == SocketMain.Probe.Depth)
        {
          chainLinkLocator.Add(SocketMain.Probe.getHash());
          locator = getNextLocation(locator);
        }

        SocketMain.Probe.push();
      }

    }
  }
}
