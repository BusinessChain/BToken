using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace BToken.Chaining
{
  class InvMessage : NetworkMessage
  {
    public List<Inventory> Inventories = new List<Inventory>();


    public InvMessage(List<Inventory> inventories)
      : base("inv")
    {
      Inventories = inventories;

      List<byte> payload = new List<byte>();

      payload.AddRange(VarInt.GetBytes(inventories.Count));

      Inventories.ForEach(
        i => payload.AddRange(i.GetBytes()));

      Payload = payload.ToArray();
    }

    public InvMessage(byte[] buffer) 
      : base(
          "inv",
          buffer)
    {
      int startIndex = 0;

      int inventoryCount = VarInt.GetInt32(
        Payload, 
        ref startIndex);

      for (int i = 0; i < inventoryCount; i++)
      {
        Inventories.Add(
          Inventory.Parse(
            Payload, 
            ref startIndex));
      }
    }    
  }
}
