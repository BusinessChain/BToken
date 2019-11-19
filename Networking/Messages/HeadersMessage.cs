using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Security.Cryptography;


namespace BToken.Networking
{
  class HeadersMessage : NetworkMessage
  {
    public List<Header> Headers { get; private set; } = new List<Header>();


    public HeadersMessage(
      List<Header> headers)
      : base("headers")
    {
      Headers = headers;
      SerializePayload();
    }
    void SerializePayload()
    {
      var payload = new List<byte>();

      payload.AddRange(VarInt.GetBytes(Headers.Count));

      foreach (Header header in Headers)
      {
        payload.AddRange(header.GetBytes());
        payload.Add(0);
      }

      Payload = payload.ToArray();
    }

    public HeadersMessage(NetworkMessage message)
      : base("headers", message.Payload)
    {
      int startIndex = 0;

      int headersCount = VarInt.GetInt32(Payload, ref startIndex);
      SHA256 sHA256 = SHA256.Create();

      for (int i = 0; i < headersCount; i += 1)
      {
        Headers.Add(
          Header.ParseHeader(
            Payload,
            ref startIndex,
            sHA256));

        startIndex += 1; // skip txCount (always zero)
      }
    }
  }
}