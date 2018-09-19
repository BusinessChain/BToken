using System;
using System.Collections.Generic;

using BToken.Networking;

namespace BToken.Chaining
{
  public class ChainBlock
  {
    public NetworkHeader Header;

    public ChainBlock BlockPrevious;
    public List<ChainBlock> BlocksNext = new List<ChainBlock>();
    public IBlockPayload BlockPayload;

    public ChainBlock(
      UInt32 version,
      UInt256 hashPrevious,
      UInt32 unixTimeSeconds,
      UInt32 nBits,
      UInt32 nonce,
      IBlockPayload payload)
    {
      Header = new NetworkHeader(
        version,
        hashPrevious,
        payload.GetPayloadHash(),
        unixTimeSeconds,
        nBits,
        nonce);

      BlockPayload = payload;
    }

    public ChainBlock(NetworkHeader header)
    {
      Header = header;
    }

  }
}
