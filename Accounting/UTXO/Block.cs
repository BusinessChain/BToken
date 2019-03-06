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
    public List<TX> TXs;
    public List<int[]> TXHashes;

    public Block(NetworkHeader header, UInt256 headerHash, List<TX> tXs, List<int[]> tXHashes)
    {
      Header = header;
      HeaderHash = headerHash;
      TXs = tXs;
      TXHashes = tXHashes;
    }
  }
}
