using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace BToken.Networking
{
  partial class NetworkAdapter
  {
    enum TypeIdentifier : UInt32
    {
      UNDEFINED = 0,
      MSG_TX = 1,
      MSG_BLOCK = 2
    }
    
    class Inventory
    {
      UInt32 Type;
      Byte[] Hash = new byte[32];

      public Inventory(UInt32 type, Byte[] hash)
      {
        Type = type;
        Hash = hash;
      }
    }
  }
}
