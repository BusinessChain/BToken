using System;
using System.Security.Cryptography;

namespace BToken.Hashing
{
  public static class SHA256d
  {
    public static byte[] Compute(byte[] data)
    {
      SHA256 SHA256Generator = SHA256.Create();
      return SHA256Generator.ComputeHash(SHA256Generator.ComputeHash(data));
    }
  }
}
