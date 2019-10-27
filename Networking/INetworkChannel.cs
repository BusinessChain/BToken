using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

using BToken.Chaining;

namespace BToken.Networking
{
  partial class Network
  {
    public interface INetworkChannel : IDisposable
    {
      List<NetworkMessage> GetApplicationMessages();

      Task<byte[]> GetHeaders(
        IEnumerable<byte[]> locatorHashes,
        CancellationToken cancellationToken);

      Task SendMessage(NetworkMessage networkMessage);

      Task<NetworkMessage> ReceiveApplicationMessage(
          CancellationToken cancellationToken);

      string GetIdentification();
    }
  }
}
