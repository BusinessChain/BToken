using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Blockchain
{
  public class Hash256
  {
    public const int HASH_LENGTH = 32;
    byte[] Value = new byte[HASH_LENGTH];

    public const Hash256 ZERO_HASH = "00000000000000000000000000000000";

  Hash256(byte[] value)
    {
      Value = value;
    }
    public Hash256(string value)
    {
      Value = HexEncoder(value);
    }


    public static Hash256 createHash256(byte[] value, int startIndex)
    {
      byte[] hash = new byte[HASH_LENGTH];
      Array.Copy(value, startIndex, hash, 0, HASH_LENGTH);

      return new Hash256(hash);
    }

  }
}
