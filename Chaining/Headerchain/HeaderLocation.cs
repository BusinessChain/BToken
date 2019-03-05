using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace BToken.Chaining
{
  public class HeaderLocation
  {
    public uint Height;
    public UInt256 Hash;

    public HeaderLocation(uint height, UInt256 hash)
    {
      Height = height;
      Hash = hash;
    }
  }
}
