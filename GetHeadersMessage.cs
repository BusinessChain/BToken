using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace BToken.Networking
{
  partial class NetworkAdapter
  {
    class GetHeadersMessage : NetworkMessage
    {
      UInt32 ProtocolVersion;
      IEnumerable<UInt256> HeaderLocator;
      UInt256 StopHash;
      
      public GetHeadersMessage(IEnumerable<UInt256> headerLocator) : base("getheaders")
      {
        ProtocolVersion = NetworkAdapter.ProtocolVersion;
        HeaderLocator = headerLocator;
        StopHash = new UInt256("00000000000000000000000000000000");

        serializePayload();
      }
      void serializePayload()
      {
        List<byte> versionPayload = new List<byte>();

        versionPayload.AddRange(BitConverter.GetBytes(ProtocolVersion));
        versionPayload.AddRange(VarInt.getBytes(HeaderLocator.Count()));

        for (int i = 0; i < HeaderLocator.Count(); i++)
        {
          versionPayload.AddRange(HeaderLocator.ElementAt(i).getBytes());
        }

        versionPayload.AddRange(StopHash.getBytes());

        Payload = versionPayload.ToArray();
      }

    }
  }
}
