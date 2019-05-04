using System;
using System.Security.Cryptography;

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
        byte[] hashPrevious,
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

  public static class NetworkHeaderExtensionMethods
  {
    public static byte[] GetHeaderHash(this Headerchain.ChainHeader header, SHA256 sHA256Generator)
    {
      if (header.HeadersNext == null)
      {
        return sHA256Generator.ComputeHash(
         sHA256Generator.ComputeHash(
           header.NetworkHeader.GetBytes()));
      }

      return header.HeadersNext[0].NetworkHeader.HashPrevious;
    }

    public static byte[] GetHeaderHash(this Headerchain.ChainHeader header)
    {
      SHA256 sHA256Generator = SHA256.Create();
      return header.GetHeaderHash(sHA256Generator);
    }
  }
}
