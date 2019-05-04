using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Runtime.Remoting.Metadata.W3cXsd2001;

namespace BToken
{
  static class StringExtensionMethods
  {
    public static byte[] ToBinary(this string hexString)
    {
      byte[] binary = SoapHexBinary.Parse(hexString).Value;
      Array.Reverse(binary);
      return binary;
    }
  }
}
