using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace BToken.Networking
{
  class InvMessage : NetworkMessage
  {
    List<Inventory> Inventories = new List<Inventory>();


    public InvMessage(NetworkMessage networkMessage) : base("inv", networkMessage.Payload)
    {
      DeserializePayload();
    }
    void DeserializePayload()
    {
      int startIndex = 0;
      int inventoryCount = VarInt.GetInt32(Payload, ref startIndex);

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
      InventoryType type = (InventoryType)BitConverter.ToUInt32(buffer, startIndex);
      startIndex += 4;

      byte[] hashBytes = new byte[32];
      Array.Copy(buffer, startIndex, hashBytes, 0, 32);
      startIndex += 32;

      return new Inventory(type, hashBytes);
    }
    
    public List<Inventory> GetBlockInventories()
    {
      return Inventories.FindAll(i => i.Type == InventoryType.MSG_BLOCK);
    }
    public List<Inventory> GetTXInventories()
    {
      return Inventories.FindAll(i => i.Type == InventoryType.MSG_TX);
    }
  }
}
