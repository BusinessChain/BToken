using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace BToken.Chaining
{
  enum InventoryType
  {
    UNDEFINED = 0,
    MSG_TX = 1,
    MSG_BLOCK = 2,
    MSG_FILTERED_BLOCK = 3,
    MSG_CMPCT_BLOCK = 4
  }

  class Inventory
  {
    public InventoryType Type;
    public byte[] Hash;

    public Inventory(InventoryType type, byte[] hash)
    {
      Type = type;
      Hash = hash;
    }

    public List<byte> GetBytes()
    {
      List<byte> bytes = new List<byte>();

      bytes.AddRange(BitConverter.GetBytes((uint)Type));
      bytes.AddRange(Hash);

      return bytes;
    }

    public static Inventory Parse(
      byte[] buffer, 
      ref int startIndex)
    {
      uint type = BitConverter.ToUInt32(buffer, startIndex);
      startIndex += 4;

      byte[] hash = new byte[32];
      Array.Copy(buffer, startIndex, hash, 0, 32);
      startIndex += 32;

      return new Inventory(
        (InventoryType)type, 
        hash);
    }

    public bool IsTX()
    {
      return Type == InventoryType.MSG_TX;
    }
  }
}
