using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace BToken.Networking
{
  public class Inventory
  {
    public enum InventoryType
    {
      UNDEFINED = 0,
      MSG_TX = 1,
      MSG_BLOCK = 2,
      MSG_FILTERED_BLOCK = 3,
      MSG_CMPCT_BLOCK = 4
    }

    public InventoryType Type { get; private set; }
    Byte[] Hash = new byte[32];

    public Inventory(uint type, Byte[] hash)
    {
      Type = (InventoryType)type;
      Hash = hash;
    }
  }
}
