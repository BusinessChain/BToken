using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;


namespace BToken.Networking
{
  class GetHeadersMessage : NetworkMessage
  {
    public IEnumerable<byte[]> HeaderLocator = new List<byte[]>();
    public byte[] StopHash = new byte[32];



    public GetHeadersMessage(
      IEnumerable<byte[]> headerLocator)
      : base("getheaders")
    {
      HeaderLocator = headerLocator;
      StopHash = 
        ("00000000000000000000000000000000" +
        "00000000000000000000000000000000").ToBinary();

      SerializePayload();
    }
    void SerializePayload()
    {
      List<byte> payload = new List<byte>();

      payload.AddRange(BitConverter.GetBytes(ProtocolVersionLocal));
      payload.AddRange(VarInt.GetBytes(HeaderLocator.Count()));

      for (int i = 0; i < HeaderLocator.Count(); i++)
      {
        payload.AddRange(HeaderLocator.ElementAt(i));
      }

      payload.AddRange(StopHash);

      Payload = payload.ToArray();
    }


    public GetHeadersMessage(NetworkMessage message)
      : base("getheaders", message.Payload)
    {
      int startIndex = 0;

      var protocolVersionRemote = BitConverter.ToUInt32(Payload, startIndex);
      startIndex += 4;

      int headersCount = VarInt.GetInt32(Payload, ref startIndex);
      for (int i = 0; i < headersCount; i++)
      {
        byte[] hash = new byte[32];
        Array.Copy(Payload, startIndex, hash, 0, 32);

        ((List<byte[]>)HeaderLocator).Add(hash);

        startIndex += 32;
      }

      Array.Copy(Payload, startIndex, StopHash, 0, 32);
      startIndex += 32;
    }

  }
}