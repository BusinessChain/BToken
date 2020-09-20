using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;

namespace BToken.Chaining
{
  class VersionMessage : NetworkMessage
  {
    public UInt32 ProtocolVersion;
    public UInt64 NetworkServicesLocal;
    public Int64 UnixTimeSeconds;
    public UInt64 NetworkServicesRemote;
    public IPAddress IPAddressRemote;
    public UInt16 PortRemote;
    public IPAddress IPAddressLocal;
    public UInt16 PortLocal;
    public UInt64 Nonce;
    public String UserAgent;
    public Int32 BlockchainHeight;
    public Byte RelayOption;



    public VersionMessage(byte[] payload) : base("version", payload)
    {
      DeserializePayload();
    }
    void DeserializePayload()
    {
      int startIndex = 0;

      ProtocolVersion = BitConverter.ToUInt32(Payload, startIndex);
      startIndex += 4;

      NetworkServicesLocal = BitConverter.ToUInt64(Payload, startIndex);
      startIndex += 8;

      UnixTimeSeconds = BitConverter.ToInt64(Payload, startIndex);
      startIndex += 8;

      NetworkServicesRemote = BitConverter.ToUInt64(Payload, startIndex);
      startIndex += 8;

      IPAddressRemote = new IPAddress(Payload.Skip(startIndex).Take(16).ToArray());
      startIndex += 16;

      PortRemote = BitConverter.ToUInt16(Payload, startIndex);
      startIndex += 2;

      // This is ignored as it is the same as above
      // NetworkServicesLocal = BitConverter.ToUInt64(Payload, startIndex);
      startIndex += 8;

      IPAddressLocal = new IPAddress(Payload.Skip(startIndex).Take(16).ToArray());
      startIndex += 16;

      PortLocal = BitConverter.ToUInt16(Payload, startIndex);
      startIndex += 2;

      Nonce = BitConverter.ToUInt64(Payload, startIndex);
      startIndex += 8;

      UserAgent = VarString.GetString(Payload, ref startIndex);

      BlockchainHeight = BitConverter.ToInt32(Payload, startIndex);
      startIndex += 4;

      if (startIndex == Payload.Length)
      {
        RelayOption = 0x01;
      }
      else
      {
        RelayOption = Payload[startIndex];
        startIndex += 1;
      }
    }

    public VersionMessage() : base("version")
    {
    }
    public void SerializePayload()
    {
      List<byte> versionPayload = new List<byte>();

      versionPayload.AddRange(BitConverter.GetBytes(ProtocolVersion));
      versionPayload.AddRange(BitConverter.GetBytes(NetworkServicesLocal));
      versionPayload.AddRange(BitConverter.GetBytes(UnixTimeSeconds));
      versionPayload.AddRange(BitConverter.GetBytes(NetworkServicesRemote));
      versionPayload.AddRange(IPAddressRemote.GetAddressBytes());
      versionPayload.AddRange(GetBytes(PortRemote));
      versionPayload.AddRange(BitConverter.GetBytes(NetworkServicesLocal));
      versionPayload.AddRange(IPAddressLocal.GetAddressBytes());
      versionPayload.AddRange(GetBytes(PortLocal));
      versionPayload.AddRange(BitConverter.GetBytes(Nonce));
      versionPayload.AddRange(VarString.GetBytes(UserAgent));
      versionPayload.AddRange(BitConverter.GetBytes(BlockchainHeight));
      versionPayload.Add(RelayOption);

      Payload = versionPayload.ToArray();
    }
    byte[] GetBytes(UInt16 uint16)
    {
      byte[] byteArray = BitConverter.GetBytes(uint16);
      Array.Reverse(byteArray);
      return byteArray;
    }
  }
}
