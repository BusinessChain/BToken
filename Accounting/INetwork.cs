using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

using BToken.Networking;

namespace BToken.Accounting
{
  public interface INetwork
  {
    Task<INetworkChannel> GetChannelAsync(CancellationToken cancellationToken);
    Task<INetworkChannel> AcceptChannelInboundSessionRequestAsync();
  }
}
