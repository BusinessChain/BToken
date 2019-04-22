using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Runtime.Remoting.Metadata.W3cXsd2001;

namespace BToken
{
  static class ByteArray2HexString
  {
    public static string ToHexString(this byte[] array)
    {
      byte[] temp = (byte[])array.Clone();
      Array.Reverse(temp);
      return new SoapHexBinary(temp).ToString();
    }
  }
}
