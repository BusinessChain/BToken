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
      
      public ChainHeader(NetworkHeader header, ChainHeader headerPrevious)
      {
        NetworkHeader = header;
        HeaderPrevious = headerPrevious;
      }
      
    }
  }

  public static class NetworkHeaderExtensionMethods
  {
    public static byte[] GetHeaderHash(this Headerchain.ChainHeader header, SHA256 sHA256)
    {
      if (header.HeadersNext == null)
      {
        return sHA256.ComputeHash(
         sHA256.ComputeHash(
           header.NetworkHeader.GetBytes()));
      }

      return header.HeadersNext[0].NetworkHeader.HashPrevious;
    }

    public static byte[] GetHeaderHash(this Headerchain.ChainHeader header)
    {
      return header.GetHeaderHash(SHA256.Create());
    }
  }
}
