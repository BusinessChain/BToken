using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace BToken.Networking
{
  class InvMessage : NetworkMessage
  {
    public List<Inventory> Inventories = new List<Inventory>();


    public InvMessage(NetworkMessage networkMessage) 
      : base(
          "inv", 
          networkMessage.Payload)
    {
      int startIndex = 0;
      int inventoryCount = VarInt.GetInt32(Payload, ref startIndex);

      for (int i = 0; i < inventoryCount; i++)
      {
        Inventories.Add(
          DeserializeInventory(
            Payload, 
            ref startIndex));
      }
    }

    Inventory DeserializeInventory(
      byte[] buffer, 
      ref int startIndex)
    {
      InventoryType type = (InventoryType)BitConverter.ToUInt32(
        buffer, 
        startIndex);

      startIndex += 4;

      byte[] hashBytes = new byte[32];
      Array.Copy(buffer, startIndex, hashBytes, 0, 32);
      startIndex += 32;

      return new Inventory(type, hashBytes);
    }
    
  }
}
