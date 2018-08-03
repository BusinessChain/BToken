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

  class InvMessage : NetworkMessage
  {
    public List<Inventory> Inventories { get; private set; } = new List<Inventory>();


    public InvMessage(NetworkMessage networkMessage) : base("inv", networkMessage.Payload)
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
      InventoryType type = (InventoryType)BitConverter.ToUInt32(buffer, startIndex);
      startIndex += 4;

      byte[] hashBytes = new byte[32];
      Array.Copy(buffer, startIndex, hashBytes, 0, 32);
      UInt256 hash = new UInt256(hashBytes);
      startIndex += 32;

      return new Inventory(type, hash);
    }

    public int GetInventoryCount()
    {
      return Inventories.Count;
    }

    public string GetInventoryType()
    {
      string type = "";
      foreach (Inventory inventory in Inventories)
      {
        string newType = inventory.Type.ToString();
        if (type != newType && type != "")
        {
          return "mixed";
        }
        type = newType;
      }
      return type;
    }

  }
}
