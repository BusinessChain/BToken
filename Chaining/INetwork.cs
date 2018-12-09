using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

using BToken.Networking;

namespace BToken.Chaining
{
  public interface INetwork
  {
    uint GetProtocolVersion();
    Task<INetworkChannel> AcceptChannelInboundRequestAsync();


    /// <summary>
    /// Sessions will be executed only if network requests are served. 
    /// </summary>
    Task ExecuteSessionAsync(INetworkSession session);
  }
}
