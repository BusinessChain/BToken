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

    public UInt256 getHash()
    {
      byte[] hashBytes = Hashing.sha256d(getBytes());
      return new UInt256(hashBytes);
    }

    byte[] getBytes()
    {
      throw new NotImplementedException();
    }
  }
}
