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
    Task<INetworkChannel> AcceptChannelInboundSessionRequestAsync();

    Task ExecuteSessionAsync(INetworkSession session, CancellationToken cancellationToken);
    Task<bool> TryExecuteSessionAsync(INetworkSession session, INetworkChannel channel, CancellationToken cancellationToken);
  }
}
