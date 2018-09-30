using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace BToken.Networking
{
  partial class Network
  {
    public class HeadersMessage : NetworkMessage
    {
      public List<NetworkHeader> Headers { get; private set; } = new List<NetworkHeader>();


      public HeadersMessage(NetworkMessage message) : base("headers", message.Payload)
      {
        int startIndex = 0;

        int headersCount = (int)VarInt.GetUInt64(Payload, ref startIndex);
        for (int i = 0; i < headersCount; i++)
        {
          Headers.Add(NetworkHeader.ParseHeader(Payload, out int txCount, ref startIndex));
        }
      }
    }
  }
}
