using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace BToken.Accounting
{
  class Block
  {
    public byte[] BlockBytes;
    public byte[] HeaderHash;
    public TX[] TXs;
    public int Height;

    public Block()
    { }

    public Block(
      byte[] headerHashBytes, 
      TX[] tXs,
      int height)
    {
      HeaderHash = headerHashBytes;
      TXs = tXs;
      Height = height;
    }
  }
}
