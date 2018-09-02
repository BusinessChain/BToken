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
    class BitcoinGenesisBlock : Blockchain.ChainBlock
    {
      public BitcoinGenesisBlock()
       : base(
          //version: 0x20000000,
          //hashPrevious: new UInt256("0000000000000000001f1a2646af39b48722a1773572189a6d10c0ed58af3f37"),
          //merkleRootHash: new UInt256("5aa4e26c799caf2941606259eb4ba7112ca580f3e6f11ecac16cf9548020c4ee"),
          //unixTimeSeconds: 1535912056,
          //nBits: 388618029,
          //nonce: 3908303163)
          version: 0x01,
          hashPrevious: new UInt256("0000000000000000000000000000000000000000000000000000000000000000"),
          merkleRootHash: new UInt256("4a5e1e4baab89f3a32518a88c31bc87f618f76673e2cc77ab2127b7afdeda33b"),
          unixTimeSeconds: 1231006505,
          nBits: 0x1d00ffff,
          nonce: 2083236893)
      { }
    }
  }
}
