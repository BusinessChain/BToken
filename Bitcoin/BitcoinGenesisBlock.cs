using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using BToken.Chaining;

namespace BToken.Bitcoin
{
  class BitcoinBlock : ChainBlock
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
          payload.GetPayloadHash(),
          unixTimeSeconds,
          nBits,
          nonce,
          new BlockArchiver.BlockStore())
    { }
  }

  class BitcoinGenesisBlock : BitcoinBlock
  {
    public BitcoinGenesisBlock()
     : base(
    #region Block 543828
    //version: 0x20000000,
    //hashPrevious: new UInt256("000000000000000000010008b376d5e7cfe1c3c43031d951fb42b8ddaf897243"),
    //unixTimeSeconds: 1538348166,
    //nBits: 388454943,
    //nonce: 286865835,
    //payload: new BitcoinBlockPayload(new UInt256("1743d8ad2df689df3a9f8c03239523a879ae88257292befb318eee7f9e59cf30")))
    #endregion

    #region Block 540288
    version: 0x20000000,
    hashPrevious: new UInt256("0000000000000000001877e616b546d1ba5cf9e8b8edd9eba480a4fbb9f02bce"),
    unixTimeSeconds: 1536290079,
    nBits: 388503969,
    nonce: 3607916943,
    payload: new BitcoinBlockPayload(new UInt256("7a76769b0b393c7df65498cf3148ad3b0a24a36aa6cf43fe0788317e75713764")))
    #endregion

    #region Block 538272
    //version: 0x20000000,
    //hashPrevious: new UInt256("0000000000000000001d9d48d93793aaa85b5f6d17c176d4ef905c7e7112b1cf"),
    //unixTimeSeconds: 1535129431,
    //nBits: 388618029,
    //nonce: 2367954839,
    //payload: new BitcoinBlockPayload(new UInt256("3ad0fa0e8c100db5831ebea7cabf6addae2c372e6e1d84f6243555df5bbfa351")))
    #endregion

    #region GenesisBlock
    //version: 0x01,
    //hashPrevious: new UInt256("0000000000000000000000000000000000000000000000000000000000000000"),
    //unixTimeSeconds: 1231006505,
    //nBits: 0x1d00ffff,
    //nonce: 2083236893,
    //payload: new BitcoinBlockPayload(new UInt256("4a5e1e4baab89f3a32518a88c31bc87f618f76673e2cc77ab2127b7afdeda33b")))
    #endregion
    { }
  }
}
