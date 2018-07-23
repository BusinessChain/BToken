using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Security.Cryptography;

namespace BToken
{
  static class Hashing
  {
    public static byte[] sha256d(byte[] data)
    {
      SHA256 SHA256Generator = SHA256.Create();
      return SHA256Generator.ComputeHash(SHA256Generator.ComputeHash(data));
    }
  }
}
