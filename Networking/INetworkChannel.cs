using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;


namespace BToken.Networking
{
  public interface INetworkChannel
  {
    Task<List<NetworkHeader>> GetHeadersAsync(List<UInt256> headerLocator);
    Task<NetworkBlock> GetBlockAsync(UInt256 hash, CancellationToken token);

    List<NetworkMessage> GetRequestMessages();
  }
}
