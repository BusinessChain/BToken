using System;
using System.Collections.Generic;

using BToken.Networking;

namespace BToken.Chaining
{
  public partial class Blockchain
  {
    public class ChainBlock
    {
      public NetworkHeader Header;

      public ChainBlock BlockPrevious;
      public List<ChainBlock> BlocksNext = new List<ChainBlock>();
      
      public ChainBlock(
        UInt32 version,
        UInt256 hashPrevious,
        UInt256 payloadHash,
        UInt32 unixTimeSeconds,
        UInt32 nBits,
        UInt32 nonce)
      {
        Header = new NetworkHeader(
          version,
          hashPrevious,
          payloadHash,
          unixTimeSeconds,
          nBits,
          nonce);
      }
      
      public ChainBlock(NetworkHeader header)
      {
        Header = header;
      }

    }
  }
}
