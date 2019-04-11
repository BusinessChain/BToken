using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using BToken.Networking;

namespace BToken.Accounting
{
  class Block
  {
    public UInt256 HeaderHash;
    public NetworkHeader Header;
    public TX[] TXs;
    public byte[][] TXHashes;

    public Block(
      NetworkHeader header,
      UInt256 headerHash, 
      TX[] tXs,
      byte[][] tXHashes)
    {
      Header = header;
      HeaderHash = headerHash;
      TXs = tXs;
      TXHashes = tXHashes;
    }
  }
}
