using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace BToken.Networking
{
  public class NetworkBlock
  {
    public NetworkHeader Header { get; private set; }
    public byte[] Payload { get; private set; }


    public NetworkBlock(NetworkHeader networkHeader, byte[] payload)
    {
      Header = networkHeader;
      Payload = payload;
    }

    public static NetworkBlock ParseBlock(byte[] blockBytes)
    {
      int startIndex = 0;

      NetworkHeader header = NetworkHeader.ParseHeader(blockBytes, out int txCount, ref startIndex);
      byte[] payload = blockBytes.Skip(startIndex).ToArray();

      return new NetworkBlock(header, payload);
    }
  }
}
