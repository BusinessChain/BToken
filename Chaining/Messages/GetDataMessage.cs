using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;


namespace BToken.Chaining
{
  class GetDataMessage : NetworkMessage
  {
    public List<Inventory> Inventories = 
      new List<Inventory>();
       


    public GetDataMessage(NetworkMessage message)
      : base("getdata", message.Payload)
    {
      int startIndex = 0;

      int inventoryCount = VarInt.GetInt32(
        Payload,
        ref startIndex);
      
      for (int i = 0; i < inventoryCount; i += 1)
      {
        Inventories.Add(
          Inventory.Parse(
            Payload,
            ref startIndex));
      }
    }

    public GetDataMessage(Inventory inventory)
      : this(new List<Inventory> { inventory })
    { }


    public GetDataMessage(List<Inventory> inventories) 
      : base("getdata")
    {
      Inventories = inventories;

      List<byte> payload = new List<byte>();

      payload.AddRange(VarInt.GetBytes(Inventories.Count()));

      for (int i = 0; i < Inventories.Count(); i++)
      {
        payload.AddRange(Inventories.ElementAt(i).GetBytes());
      }

      Payload = payload.ToArray();
    }
  }
}
