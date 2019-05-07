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

    public Block()
    { }
  }
}
