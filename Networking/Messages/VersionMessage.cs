using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;

namespace BToken.Networking
{
  partial class NetworkAdapter
  {
    class VersionMessage : NetworkMessage
    {
      public UInt32 ProtocolVersion { get; private set; }
      public UInt64 NetworkServicesLocal { get; private set; }
      public Int64 UnixTimeSeconds { get; private set; }
      public UInt64 NetworkServicesRemote { get; private set; }
      public IPAddress IPAddressRemote { get; private set; }
      public UInt16 PortRemote { get; private set; }
      public IPAddress IPAddressLocal { get; private set; }
      public UInt16 PortLocal { get; private set; }
      public UInt64 Nonce { get; private set; }
      public String UserAgent { get; private set; }
      public UInt32 BlockchainHeight { get; private set; }
      public Byte RelayOption { get; private set; }



      public VersionMessage(byte[] payload) : base("version", payload)
      {
        deserializePayload();
      }
      void deserializePayload()
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

        UserAgent = VarString.getString(Payload, ref startIndex);

        BlockchainHeight = BitConverter.ToUInt32(Payload, startIndex);
        startIndex += 4;

        if(startIndex == Payload.Length)
        {
          RelayOption = 0x01;
        }
        else
        {
          RelayOption = Payload[startIndex];
          startIndex += 1;
        }
      }

      public VersionMessage(UInt32 blockchainHeight) : base("version")
      {
        ProtocolVersion = NetworkAdapter.ProtocolVersion;
        NetworkServicesLocal = (UInt64)NetworkServicesLocalProvided;
        UnixTimeSeconds = getUnixTimeSeconds();
        NetworkServicesRemote = (UInt64)NetworkServicesRemoteRequired;
        IPAddressRemote = IPAddress.Loopback.MapToIPv6();
        PortRemote = NetworkAdapter.Port;
        IPAddressLocal = IPAddress.Loopback.MapToIPv6();
        PortLocal = NetworkAdapter.Port;
        Nonce = NetworkAdapter.Nonce;
        UserAgent = NetworkAdapter.UserAgent;
        BlockchainHeight = blockchainHeight;
        RelayOption = NetworkAdapter.RelayOption;
        
        serializePayload();
      }
      void serializePayload()
      {
        List<byte> versionPayload = new List<byte>();

        versionPayload.AddRange(BitConverter.GetBytes(ProtocolVersion));
        versionPayload.AddRange(BitConverter.GetBytes(NetworkServicesLocal));
        versionPayload.AddRange(BitConverter.GetBytes(UnixTimeSeconds));
        versionPayload.AddRange(BitConverter.GetBytes(NetworkServicesRemote));
        versionPayload.AddRange(IPAddressRemote.GetAddressBytes());
        versionPayload.AddRange(GetBytesBigEndian(PortRemote));
        versionPayload.AddRange(BitConverter.GetBytes(NetworkServicesLocal));
        versionPayload.AddRange(IPAddressLocal.GetAddressBytes());
        versionPayload.AddRange(GetBytesBigEndian(PortLocal));
        versionPayload.AddRange(BitConverter.GetBytes(Nonce));
        versionPayload.AddRange(VarString.getBytes(UserAgent));
        versionPayload.AddRange(BitConverter.GetBytes(BlockchainHeight));
        versionPayload.Add(RelayOption);

        Payload = versionPayload.ToArray();
      }
      byte[] GetBytesBigEndian(UInt32 uint32)
      {
        byte[] byteArray = BitConverter.GetBytes(uint32);
        Array.Reverse(byteArray);
        return byteArray;
      }
    }
  }
}
