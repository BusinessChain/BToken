using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace BToken.Networking
{
  public interface INetworkSession
  {
    Task RunAsync(INetworkChannel channel, CancellationToken cancellationToken);
    string GetSessionID();
  }
}
