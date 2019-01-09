using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using BToken.Networking;

namespace BToken.Accounting.UTXO
{
  class BitcoinGenesisBlock : NetworkBlock
  {
    public BitcoinGenesisBlock()
     : base(new BitcoinGenesisHeader(), 1, new byte[100])
    { }
  }

  class BitcoinGenesisHeader : NetworkHeader
  {
    public BitcoinGenesisHeader()
     : base(
    #region Block 543828
    //version: 0x20000000,
    //hashPrevious: new UInt256("000000000000000000010008b376d5e7cfe1c3c43031d951fb42b8ddaf897243"),
    //payloadHash: new UInt256("1743d8ad2df689df3a9f8c03239523a879ae88257292befb318eee7f9e59cf30"),
    //unixTimeSeconds: 1538348166,
    //nBits: 388454943,
    //nonce: 286865835)
    #endregion

    #region Block 504000
    //version: 0x20000000,
    //hashPrevious: new UInt256("000000000000000000720da39f66f29337b9a29223e1ce05fd5ee57bb72a9223"),
    //merkleRootHash: new UInt256("6195fe0cded5aeb07c8d36826758343778ccd81e4285bba0f76e35e8549ab93c"),
    //unixTimeSeconds: 1515827554,
    //nBits: 394155916,
    //nonce: 3750147913)
    #endregion

    #region GenesisBlock
    version: 0x01,
    hashPrevious: new UInt256("0000000000000000000000000000000000000000000000000000000000000000"),
    merkleRootHash: new UInt256("4a5e1e4baab89f3a32518a88c31bc87f618f76673e2cc77ab2127b7afdeda33b"),
    unixTimeSeconds: 1231006505,
    nBits: 0x1d00ffff,
    nonce: 2083236893)
    #endregion
    { }
  }
}
