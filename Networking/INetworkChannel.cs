using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;


namespace BToken.Networking
{
  partial class Network
  {
    public interface INetworkChannel : IDisposable
    {
      Task<byte[]> GetHeaders(
        IEnumerable<byte[]> locatorHashes);

      Task<byte[]> ReceiveBlock();

      Task SendMessage(NetworkMessage networkMessage);

      Task<NetworkMessage> ReceiveMessage(
          CancellationToken cancellationToken);
      
      string GetIdentification();

      void ReportDuplicate();

      bool IsDisposed();

      bool IsInbound();
    }
  }
}
