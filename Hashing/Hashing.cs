using System;
using System.Security.Cryptography;

namespace BToken
{
  public static class Hashing
  {
    public static byte[] SHA256d(byte[] data)
    {
      SHA256 SHA256Generator = SHA256.Create();
      return SHA256Generator.ComputeHash(SHA256Generator.ComputeHash(data));
    }
  }
}
