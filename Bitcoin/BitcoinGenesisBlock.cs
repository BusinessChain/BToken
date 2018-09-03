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
          version: 0x20000000,
          hashPrevious: new UInt256("0000000000000000001895f7e2cec7e94cf3979fdd9b4958b94279127f5f10ed"),
          merkleRootHash: new UInt256("20488af25c55f543620ccc0baff35eca07d9877239dc9cdd9f600786edfc1f0d"),
          unixTimeSeconds: 1535970668,
          nBits: 388618029,
          nonce: 2777012598)
          //version: 0x01,
          //hashPrevious: new UInt256("0000000000000000000000000000000000000000000000000000000000000000"),
          //merkleRootHash: new UInt256("4a5e1e4baab89f3a32518a88c31bc87f618f76673e2cc77ab2127b7afdeda33b"),
          //unixTimeSeconds: 1231006505,
          //nBits: 0x1d00ffff,
          //nonce: 2083236893)
      { }
    }
  }
}
