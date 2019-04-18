using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace BToken.Chaining
{
  public class HeaderLocation
  {
    public int Height;
    public UInt256 Hash;

    public HeaderLocation(int height, UInt256 hash)
    {
      Height = height;
      Hash = hash;
    }
  }
}
