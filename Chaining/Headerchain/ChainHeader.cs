using System;
using System.Collections.Generic;
using System.Linq;


namespace BToken.Chaining
{
  partial class Blockchain : Chain
  {
    public partial class Headerchain
    {
      public partial class ChainHeader : ChainLink
      {
        public UInt64 UnixTimeSeconds;
        public UInt256 MerkleRootHash;
        public UInt32 NBits;


        public ChainHeader(
          UInt256 hash,
          UInt256 hashPrevious,
          UInt32 nBits,
          UInt256 merkleRootHash,
          UInt64 unixTimeSeconds)
          : base
          (
            hash: hash,
            hashPrevious: hashPrevious
          )
        {
          UnixTimeSeconds = unixTimeSeconds;
          MerkleRootHash = merkleRootHash;
          NBits = nBits;
        }

        public ChainHeader GetNextHeader(UInt256 hash)
        {
          return (ChainHeader)GetNextChainLink(hash);
        }
        public ChainHeader getHeaderPrevious()
        {
          return getHeaderPrevious(1);
        }
        public ChainHeader getHeaderPrevious(uint depth)
        {
          return (ChainHeader)getChainLinkPrevious(depth);
        }
        
      }
    }
  }
}
