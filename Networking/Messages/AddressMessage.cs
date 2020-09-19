using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace BToken.Chaining
{
  class AddressMessage : NetworkMessage
  {
    public List<NetworkAddress> NetworkAddresses = 
      new List<NetworkAddress>();


    public AddressMessage(byte[] messagePayload) 
      : base("addr", messagePayload)
    {
      int startIndex = 0;

      int addressesCount = VarInt.GetInt32(
        Payload, 
        ref startIndex);

      for (int i = 0; i < addressesCount; i++)
      {
        NetworkAddresses.Add(
          NetworkAddress.ParseAddress(
            Payload, ref startIndex));
      }
    }
  }
}
