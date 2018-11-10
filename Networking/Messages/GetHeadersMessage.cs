﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace BToken.Networking
{
  partial class Network
  {
    class GetHeadersMessage : NetworkMessage
    {
      UInt32 ProtocolVersion;
      List<UInt256> HeaderLocator = new List<UInt256>();
      UInt256 StopHash;
      
      public GetHeadersMessage(List<UInt256> headerLocator) : base("getheaders")
      {
        ProtocolVersion = Network.ProtocolVersion;
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
}
