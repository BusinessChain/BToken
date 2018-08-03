using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace BToken.Networking
{
  class Inventory
  {

    public InventoryType Type { get; private set; }
    public UInt256 Hash { get; private set; }

    public Inventory(InventoryType type, UInt256 hash)
    {
      Type = type;
      Hash = hash;
    }

    public List<byte> getBytes()
    {
      List<byte> bytes = new List<byte>();

      bytes.AddRange(BitConverter.GetBytes((UInt32)Type));

      byte[] hashBytes = Hash.GetBytes();
      Array.Reverse(hashBytes);
      bytes.AddRange(hashBytes);

      return bytes;

    }

  }
}
