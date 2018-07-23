using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace BToken.Networking
{
  partial class NetworkAdapter
  {
    class HeadersMessage : NetworkMessage
    {
      public const int MAX_HEADER_COUNT = 2000;
      public List<NetworkHeader> NetworkHeaders { get; private set; } = new List<NetworkHeader>();
      
      
      public HeadersMessage(byte[] payload) : base("headers")
      {
        deserializePayload();
      }
      void deserializePayload()
      {
        int startIndex = 0;

        int headersCount = (int)VarInt.getUInt64(Payload, ref startIndex);

        for (int i = 0; i < headersCount; i++)
        {
          byte[] header = new byte[NetworkHeader.HEADER_LENGTH];
          Array.Copy(Payload, startIndex, header, 0, NetworkHeader.HEADER_LENGTH);
          startIndex = startIndex + NetworkHeader.HEADER_LENGTH;

          NetworkHeaders.Add(NetworkHeader.deserialize(header));
        }
      }
      
      public bool hasMaxHeaderCount()
      {
        return NetworkHeaders.Count == MAX_HEADER_COUNT;
      }
      
      public bool attachesToHeaderLocator(IEnumerable<UInt256> headerLocator)
      {
        return headerLocator.Any(h => h.isEqual(NetworkHeaders.First().PreviousHeaderHash));
      }
    }
  }
}
