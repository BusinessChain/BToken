using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Runtime.Remoting.Metadata.W3cXsd2001;

namespace BToken.Chaining
{
  public class HeaderLocation
  {
    public int Height;
    public byte[] Hash;

    public HeaderLocation(int height, byte[] hash)
    {
      Height = height;
      Hash = hash;
    }

    public HeaderLocation(int height, string hash)
      : this(
          height,
          hash.ToBinary())
    { }
  }
}
