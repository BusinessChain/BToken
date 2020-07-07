using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;

namespace BToken.Chaining
{
  partial class VersionMessage : NetworkMessage
  {
    public const UInt32 ProtocolVersion = 70015;
    public const UInt64 NetworkServicesRemote = 
      (long)ServiceFlags.NODE_NETWORK;
    public const UInt64 NetworkServicesLocal =
      (long)ServiceFlags.NODE_NETWORK;
    public static readonly IPAddress IPAddressRemote = 
      IPAddress.Loopback.MapToIPv6();
    public static 
      readonly IPAddress IPAddressLocal =
      IPAddress.Loopback.MapToIPv6();
    public const UInt16 PortRemote = 8333;
    public const UInt16 PortLocal = PortRemote;
    public const String UserAgent = "/BToken:0.0.0/";
    public static readonly UInt64 Nonce = CreateNonce();
    static ulong CreateNonce()
    {
      Random rnd = new Random();

      ulong number = (ulong)rnd.Next();
      number = number << 32;
      return number |= (uint)rnd.Next();
    }

    public Int64 UnixTimeSeconds;
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

      BlockchainHeight = BitConverter.ToUInt32(Payload, startIndex);
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
      ProtocolVersion = Network.ProtocolVersion;
      NetworkServicesLocal = (long)NetworkServicesLocalProvided;
      UnixTimeSeconds = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
      NetworkServicesRemote = (long)NetworkServicesRemoteRequired;
      IPAddressRemote = IPAddress.Loopback.MapToIPv6();
      PortRemote = Port;
      IPAddressLocal = IPAddress.Loopback.MapToIPv6();
      PortLocal = Port;
      Nonce = Network.Nonce;
      UserAgent = Network.UserAgent;
      BlockchainHeight = 0; // We do not have that information at this point. I think this can be ignored.
      RelayOption = Network.RelayOption;

      SerializePayload();
    }
    void SerializePayload()
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
