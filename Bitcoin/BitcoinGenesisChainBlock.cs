using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using BToken.Chaining;

namespace BToken
{
  partial class Bitcoin
  {
    class BitcoinGenesisChainBlock : Blockchain.ChainBlock
    {
      public BitcoinGenesisChainBlock()
       : base(
           new NetworkHeader(
             version: 0x01,
             hashPrevious: new UInt256("0000000000000000000000000000000000000000000000000000000000000000"),
             merkleRootHash: new UInt256("4a5e1e4baab89f3a32518a88c31bc87f618f76673e2cc77ab2127b7afdeda33b"),
             unixTimeSeconds: 1231006505,
             nBits: 0x1d00ffff,
             nonce: 2083236893))
      { }
    }
  }
}
