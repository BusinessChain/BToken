using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace BToken.Chaining
{
  public class BlockLocation
  {
    public uint Height;
    public UInt256 Hash;

    public BlockLocation(uint height, UInt256 hash)
    {
      Height = height;
      Hash = hash;
    }
  }
}
