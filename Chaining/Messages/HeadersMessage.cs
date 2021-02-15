using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Security.Cryptography;


namespace BToken.Chaining
{
  class HeadersMessage : NetworkMessage
  {
    public List<Header> Headers = new List<Header>();


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
  }
}