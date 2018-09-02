using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace BToken.Networking
{
  class NetworkBlock
  {
    public NetworkHeader Header { get; private set; }
    public List<NetworkTX> NetworkTXs { get; private set; }


    public NetworkBlock(NetworkHeader networkHeader, List<NetworkTX> networkTXs)
    {
      Header = networkHeader;
      NetworkTXs = networkTXs;
    }

    public static NetworkBlock ParseBlock(byte[] blockBytes)
    {
      int startIndex = 0;

      NetworkHeader header = NetworkHeader.ParseHeader(blockBytes, out int txCount, ref startIndex);

      var networkTXs = new List<NetworkTX>();
      for (int i = 0; i < txCount; i++)
      {
        networkTXs.Add(NetworkTX.Parse(blockBytes, ref startIndex));
      }

      return new NetworkBlock(header, networkTXs);
    }
  }
}
