using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace BToken.Networking
{
  class HeadersMessage : NetworkMessage
  {
    public const int MAX_HEADER_COUNT = 2000;
    public List<NetworkHeader> NetworkHeaders { get; private set; } = new List<NetworkHeader>();


    public HeadersMessage(NetworkMessage message) : base("headers", message.Payload)
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

        NetworkHeaders.Add(ParseHeader(header));
      }
    }
    public static NetworkHeader ParseHeader(byte[] header)
    {
      int startIndex = 0;
      byte[] tempByteArray = new byte[UInt256.BYTE_LENGTH];

      UInt32 version = BitConverter.ToUInt32(header, startIndex);
      startIndex += 4;

      Array.Copy(header, startIndex, tempByteArray, 0, UInt256.BYTE_LENGTH);
      UInt256 previousHeaderHash = new UInt256(tempByteArray);
      startIndex += UInt256.BYTE_LENGTH;

      Array.Copy(header, startIndex, tempByteArray, 0, UInt256.BYTE_LENGTH);
      UInt256 merkleRootHash = new UInt256(tempByteArray);
      startIndex += UInt256.BYTE_LENGTH;

      UInt32 unixTimeSeconds = BitConverter.ToUInt32(header, startIndex);
      startIndex += 4;

      UInt32 nBits = BitConverter.ToUInt32(header, startIndex);
      startIndex += 4;

      UInt32 nonce = BitConverter.ToUInt32(header, startIndex);
      startIndex += 4;

      Byte txnCount = header[startIndex];
      startIndex += 1;

      return new NetworkHeader(version, previousHeaderHash, merkleRootHash, unixTimeSeconds, nBits, nonce, txnCount);
    }

    public bool hasMaxHeaderCount()
    {
      return NetworkHeaders.Count == MAX_HEADER_COUNT;
    }

    public bool connectsToHeaderLocator(IEnumerable<UInt256> headerLocator)
    {
      return headerLocator.Any(h => h.isEqual(NetworkHeaders.First().HashPrevious));
    }
  }
}
