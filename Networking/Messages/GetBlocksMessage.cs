﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace BToken.Networking
{
  partial class Network
  {
    class GetBlocksMessage : NetworkMessage
    {
      public UInt32 ProtocolVersion { get; private set; }
      public List<byte[]> BlockLocator { get; private set; }
      public byte[] StopHash { get; private set; } = new byte[32] { 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0 };


      public GetBlocksMessage(List<byte[]> blockLocator) : base("getblocks")
      {
        ProtocolVersion = Network.ProtocolVersion;
        BlockLocator = blockLocator;

        serializePayload();
      }
      void serializePayload()
      {
        List<byte> versionPayload = new List<byte>();

        versionPayload.AddRange(BitConverter.GetBytes(ProtocolVersion));
        versionPayload.AddRange(VarInt.GetBytes(BlockLocator.Count));

        for (int i = 0; i < BlockLocator.Count; i++)
        {
          versionPayload.AddRange(BlockLocator[i]);
        }

        versionPayload.AddRange(StopHash);

        Payload = versionPayload.ToArray();
      }

    }
  }
}
