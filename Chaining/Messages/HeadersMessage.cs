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
      SerializePayload();
    }
    void SerializePayload()
    {
      var payload = new List<byte>();

      payload.AddRange(VarInt.GetBytes(Headers.Count));

      foreach(NetworkHeader header in Headers)
      {
        payload.AddRange(header.GetBytes());
        payload.Add(0x00);
      }

      Payload = payload.ToArray();
    }

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
