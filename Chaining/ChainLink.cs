using System.Collections.Generic;
using System.Linq;
using System;

namespace BToken.Chaining
{
  abstract class ChainLink
  {
    ChainLink ChainLinkPrevious;
    public List<ChainLink> NextChainLinks { get; private set; } = new List<ChainLink>();

    public ChainLink GetChainLink(UInt256 hash)
    {
      return NextChainLinks.Find(c => c.getHash() == hash);
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
      return NextChainLinks.Any(c => c.getHash() == chainLinkNext.getHash());
    }
    public virtual void connectToPrevious(ChainLink chainLinkPrevious)
    {
      ChainLinkPrevious = chainLinkPrevious;
    }
    public bool isGenesis()
    {
      return getHeight() == 0;
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
    public abstract UInt256 getHashPrevious();
    public abstract UInt256 getHash();
    public abstract uint getHeight();
    public abstract double getAccumulatedDifficulty();
  }
}
