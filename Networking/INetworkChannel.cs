using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

using BToken.Chaining;

namespace BToken.Networking
{
  public interface INetworkChannel
  {
    Task<List<NetworkHeader>> GetHeadersAsync(List<UInt256> headerLocator);
    Task RequestBlocksAsync(List<UInt256> headerHashes);
    Task<NetworkMessage> GetNetworkMessageAsync(CancellationToken token);
  }
}
