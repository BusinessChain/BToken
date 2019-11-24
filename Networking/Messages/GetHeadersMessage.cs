using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;


namespace BToken.Networking
{
  class GetHeadersMessage : NetworkMessage
  {
    public uint ProtocolVersion;
    public IEnumerable<byte[]> HeaderLocator = new List<byte[]>();
    public UInt256 StopHash;



    public GetHeadersMessage(
      IEnumerable<byte[]> headerLocator,
      uint protocolVersion)
      : base("getheaders")
    {
      ProtocolVersion = protocolVersion;
      HeaderLocator = headerLocator;
      StopHash = new UInt256("0000000000000000000000000000000000000000000000000000000000000000");

      SerializePayload();
    }
    void SerializePayload()
    {
      List<byte> payload = new List<byte>();

      payload.AddRange(BitConverter.GetBytes(ProtocolVersion));
      payload.AddRange(VarInt.GetBytes(HeaderLocator.Count()));

      for (int i = 0; i < HeaderLocator.Count(); i++)
      {
        payload.AddRange(HeaderLocator.ElementAt(i));
      }

      payload.AddRange(StopHash.GetBytes());

      Payload = payload.ToArray();
    }


    public GetHeadersMessage(NetworkMessage message)
      : base("getheaders", message.Payload)
    {
      int startIndex = 0;

      ProtocolVersion = BitConverter.ToUInt32(Payload, startIndex);
      startIndex += 4;

      int headersCount = VarInt.GetInt32(Payload, ref startIndex);
      for (int i = 0; i < headersCount; i++)
      {
        byte[] hash = new byte[32];
        Array.Copy(Payload, startIndex, hash, 0, 32);

        ((List<byte[]>)HeaderLocator).Add(hash);

        startIndex += 32;
      }

      StopHash = new UInt256(Payload, ref startIndex);
    }

  }
}