using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;


namespace BToken.Networking
{
  class GetDataMessage : NetworkMessage
  {
    public IEnumerable<Inventory> Inventories { get; private set; }


    public GetDataMessage(List<Inventory> inventories) : base("getdata")
    {
      Inventories = inventories;

      serializePayload();
    }
    void serializePayload()
    {
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
