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
    List<NetworkMessage> GetInboundRequestMessages();
    Task<bool> TryExecuteSessionAsync(INetworkSession session, CancellationToken cancellationToken);
    Task SendMessageAsync(NetworkMessage message);
    Task<NetworkMessage> ReceiveSessionMessageAsync(CancellationToken cancellationToken);
    uint GetProtocolVersion();

    string GetIdentification();
  }
}
