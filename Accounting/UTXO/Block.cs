﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using BToken.Networking;

namespace BToken.Accounting
{
  class Block
  {
    public byte[] HeaderHashBytes;
    public TX[] TXs;
    public byte[][] TXHashes;
    public byte[] BlockBytes;

    public Block(
      byte[] headerHashBytes, 
      TX[] tXs,
      byte[][] tXHashes,
      byte[] blockBytes)
    {
      HeaderHashBytes = headerHashBytes;
      TXs = tXs;
      TXHashes = tXHashes;
      BlockBytes = blockBytes;
    }
  }
}
