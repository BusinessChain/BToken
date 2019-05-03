using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace BToken.Networking
{
  enum InventoryType : UInt32
  {
    UNDEFINED = 0,
    MSG_TX = 1,
    MSG_BLOCK = 2,
    MSG_FILTERED_BLOCK = 3,
    MSG_CMPCT_BLOCK = 4
  }

  class Inventory
  {

    public InventoryType Type { get; private set; }
    public byte[] Hash { get; private set; }

    public Inventory(InventoryType type, byte[] hash)
    {
      Type = type;
      Hash = hash;
    }

    public List<byte> getBytes()
    {
      List<byte> bytes = new List<byte>();

      bytes.AddRange(BitConverter.GetBytes((uint)Type));
      bytes.AddRange(Hash);

      return bytes;

    }

  }
}
