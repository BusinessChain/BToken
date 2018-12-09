using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using BToken.Networking;

namespace BToken.Chaining
{
  class GetHeadersMessage : NetworkMessage
  {
    public UInt32 ProtocolVersion { get; private set; }
    public List<UInt256> HeaderLocator { get; private set; }  = new List<UInt256>();
    public UInt256 StopHash { get; private set; }

    public GetHeadersMessage(List<UInt256> headerLocator, uint protocolVersion) : base("getheaders")
    {
      ProtocolVersion = protocolVersion;
      HeaderLocator = headerLocator;
      StopHash = new UInt256("0000000000000000000000000000000000000000000000000000000000000000");

      serializePayload();
    }
    void serializePayload()
    {
      List<byte> payload = new List<byte>();

      payload.AddRange(BitConverter.GetBytes(ProtocolVersion));
      payload.AddRange(VarInt.GetBytes(HeaderLocator.Count()));

      for (int i = 0; i < HeaderLocator.Count(); i++)
      {
        payload.AddRange(HeaderLocator.ElementAt(i).GetBytes());
      }

      payload.AddRange(StopHash.GetBytes());

      Payload = payload.ToArray();
    }


    public GetHeadersMessage(NetworkMessage message) : base("getheaders", message.Payload)
    {
      int startIndex = 0;

      ProtocolVersion = BitConverter.ToUInt32(Payload, startIndex);
      startIndex += 4;

      int headersCount = (int)VarInt.GetUInt64(Payload, ref startIndex);
      for (int i = 0; i < headersCount; i++)
      {
        HeaderLocator.Add(new UInt256(Payload, ref startIndex));
      }

      StopHash = new UInt256(Payload, ref startIndex);
    }

  }
}
