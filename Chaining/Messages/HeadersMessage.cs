using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

using BToken.Networking;

namespace BToken.Chaining
{
  class HeadersMessage : NetworkMessage
  {
    public List<NetworkHeader> Headers { get; private set; } = new List<NetworkHeader>();


    public HeadersMessage(List<NetworkHeader> headers) : base("headers")
    {
      Headers = headers;
    }
    // interface or abstract class with method serilize()

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
