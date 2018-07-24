using System.Collections.Generic;
using System.Linq;
using System;

namespace BToken.Chaining
{
  abstract class ChainLink
  {
    public UInt256 Hash { get; private set; }
    public UInt256 HashPrevious { get; private set; }
    public uint Height { get; private set; }

    ChainLink ChainLinkPrevious;
    List<ChainLink> NextChainLinks = new List<ChainLink>();

    protected ChainLink() { }
    protected ChainLink(UInt256 hash, UInt256 hashPrevious)
    {
      Hash = hash;
      HashPrevious = hashPrevious;
    }

    public ChainLink GetNextChainLink(UInt256 hash)
    {
      return NextChainLinks.Find(c => c.Hash == hash);
    }
    public ChainLink getChainLinkPrevious()
    {
      return ChainLinkPrevious;
    }
    public ChainLink getChainLinkPrevious(uint depth)
    {
      if (depth > 0)
      {
        if (isGenesis())
        {
          throw new ArgumentOutOfRangeException("Genesis Link encountered prior specified depth has been reached.");
        }

        return ChainLinkPrevious.getChainLinkPrevious(--depth);
      }

      return this;
    }
    public void connectToNext(ChainLink chainLinkNext)
    {
      NextChainLinks.Add(chainLinkNext);
    }
    public bool isConnectedToNext(ChainLink chainLinkNext)
    {
      return NextChainLinks.Any(c => c.Hash == chainLinkNext.Hash);
    }
    public virtual void connectToPrevious(ChainLink chainLinkPrevious)
    {
      Height = Height + 1;
      ChainLinkPrevious = chainLinkPrevious;
    }
    public bool isGenesis()
    {
      return Height == 0;
    }

    public bool isStrongerThan(ChainLink chainLink)
    {
      if(chainLink == null)
      {
        return true;
      }

      return getAccumulatedDifficulty() > chainLink.getAccumulatedDifficulty();
    }

    public abstract void validate();
    public abstract double getAccumulatedDifficulty();
  }
}
