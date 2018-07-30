using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace BToken.Networking
{
  partial class NetworkAdapter
  {
    class InvMessage : NetworkMessage
    {
      List<Inventory> Inventories = new List<Inventory>();

      
      public InvMessage(byte[] payload) : base("inv")
      {
        deserializePayload();
      }
      void deserializePayload()
      {
        int startIndex = 0;

        int inventoryCount = (int)VarInt.getUInt64(Payload, ref startIndex);

        deserializeInventories(Payload, ref startIndex, inventoryCount);
      }
      void deserializeInventories(byte[] buffer, ref int startIndex, int inventoryCount)
      {
        for (int i = 0; i < inventoryCount; i++)
        {
          Inventory inventory = deserializeInventory(buffer, ref startIndex);
          Inventories.Add(inventory);
        }
      }
      Inventory deserializeInventory(byte[] buffer, ref int startIndex)
      {
        UInt32 type = BitConverter.ToUInt32(buffer, startIndex);
        startIndex += 4;

        byte[] hash = new byte[32];
        Array.Copy(buffer, startIndex, hash, 0, 32);
        startIndex += 32;

        return new Inventory(type, hash);
      }
    }
  }
}
