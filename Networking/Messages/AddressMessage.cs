using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace BToken.Networking
{
  partial class Network
  {
    class AddressMessage : NetworkMessage
    {
      public List<NetworkAddress> NetworkAddresses { get; private set; } = new List<NetworkAddress>();


      public AddressMessage(NetworkMessage networkMessage) : base("addr", networkMessage.Payload)
      {
        DeserializePayload();
      }
      void DeserializePayload()
      {
        int startIndex = 0;

        int addressesCount = VarInt.GetInt32(Payload, ref startIndex);
        for (int i = 0; i < addressesCount; i++)
        {
          NetworkAddresses.Add(NetworkAddress.ParseAddress(Payload, ref startIndex));
        }
      }
    }
  }
}
