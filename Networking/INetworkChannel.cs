using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;


namespace BToken.Networking
{
  public interface INetworkChannel : IDisposable
  {
    List<NetworkMessage> GetRequestMessages();
    Task<bool> TryExecuteSessionAsync(INetworkSession session, CancellationToken cancellationToken);

    Task SendMessageAsync(NetworkMessage message);
    Task<NetworkMessage> ReceiveMessageAsync(CancellationToken cancellationToken);
  }
}
