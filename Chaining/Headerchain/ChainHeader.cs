using System;
using System.Collections.Generic;

using BToken.Networking;

namespace BToken.Chaining
{
  public partial class Headerchain
  {
    public class ChainHeader
    {
      public NetworkHeader NetworkHeader;

      public ChainHeader HeaderPrevious;
      public ChainHeader[] HeadersNext;

      public ChainHeader(
        UInt32 version,
        UInt256 hashPrevious,
        byte[] payloadHash,
        UInt32 unixTimeSeconds,
        UInt32 nBits,
        UInt32 nonce)
      {
        NetworkHeader = new NetworkHeader(
          version,
          hashPrevious,
          payloadHash,
          unixTimeSeconds,
          nBits,
          nonce);
      }

      public ChainHeader(NetworkHeader header, ChainHeader headerPrevious)
      {
        NetworkHeader = header;
        HeaderPrevious = headerPrevious;
      }

    }
  }
}
