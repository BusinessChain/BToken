using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace BToken.Networking
{
  public interface INetworkSession
  {
    Task StartAsync(INetworkChannel channel);
  }
}
