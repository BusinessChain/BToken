using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using BToken.Networking;

namespace BToken.Chaining
{
  class ChainBlock : ChainLink
  {
    public ChainHeader Header { get; private set; }
    public List<TX> TXs { get; private set; }

    public ChainBlock(NetworkBlock networkBlock)
    {
      Header = new ChainHeader(networkBlock.Header);
      TXs = networkBlock.NetworkTXs.Select(ntx => new TX(ntx)).ToList();
    }
    public ChainBlock(ChainHeader header, List<TX> tXs)
    {
      Header = header;
      TXs = tXs;
    }

    public override void connectToPrevious(ChainLink chainLinkPrevious)
    {
      base.connectToPrevious(chainLinkPrevious);

      ChainBlock blockPrevious = (ChainBlock)chainLinkPrevious;

      Header = blockPrevious.Header.GetNextHeader(getHash());
    }

    public override UInt256 getHashPrevious()
    {
      return Header.getHashPrevious();
    }
    public override UInt256 getHash()
    {
      return Header.getHash();
    }
    public override uint getHeight()
    {
      return Header.getHeight();
    }
    public override double getAccumulatedDifficulty()
    {
      return Header.getAccumulatedDifficulty();
    }
    public override void validate()
    {
      if (!Header.getMerkleRootHash().isEqual(ComputeMerkleRootHash()))
      {
        throw new ChainLinkException(this, ChainLinkCode.INVALID);
      }
    }
    UInt256 ComputeMerkleRootHash()
    {
      throw new NotImplementedException();
    }
  }
}
