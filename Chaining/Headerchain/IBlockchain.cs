using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

using BToken.Networking;

namespace BToken.Chaining
{
  public interface IBlockchain
  {
    Task InitialBlockDownloadAsync();
    void DownloadBlock(NetworkHeader header);

    Task<INetworkSession> RequestSessionAsync(NetworkMessage networkMessage, CancellationToken cancellationToken);
  }
}
