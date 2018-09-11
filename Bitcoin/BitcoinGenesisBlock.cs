using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using BToken.Chaining;

namespace BToken.Bitcoin
{
  class BitcoinBlock : Blockchain.ChainBlock
  {
    public BitcoinBlock(
      UInt32 version,
      UInt256 hashPrevious,
      UInt32 unixTimeSeconds,
      UInt32 nBits,
      UInt32 nonce,
      BitcoinBlockPayload payload)
      : base(
          version,
          hashPrevious,
          unixTimeSeconds,
          nBits,
          nonce,
          payload)
    { }
  }

  class BitcoinGenesisBlock : BitcoinBlock
  {
    public BitcoinGenesisBlock()
     : base(
    //version: 0x20000000,
    //hashPrevious: new UInt256("0000000000000000001877e616b546d1ba5cf9e8b8edd9eba480a4fbb9f02bce"),
    //unixTimeSeconds: 1536290079,
    //nBits: 388503969,
    //nonce: 3607916943,
    //payload: new BitcoinBlockPayload(new UInt256("7a76769b0b393c7df65498cf3148ad3b0a24a36aa6cf43fe0788317e75713764")))
    version: 0x01,
    hashPrevious: new UInt256("0000000000000000000000000000000000000000000000000000000000000000"),
    unixTimeSeconds: 1231006505,
    nBits: 0x1d00ffff,
    nonce: 2083236893,
    payload: new BitcoinBlockPayload(new UInt256("4a5e1e4baab89f3a32518a88c31bc87f618f76673e2cc77ab2127b7afdeda33b")))
    { }
  }
}
