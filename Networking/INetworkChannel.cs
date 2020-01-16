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

      Task RequestBlocks(List<byte[]> hashes);

      Task<byte[]> ReceiveBlock();

      Task SendMessage(NetworkMessage networkMessage);

      Task<NetworkMessage> ReceiveMessage(
          CancellationToken cancellationToken,
          string messageType);

      void Release();

      string GetIdentification();

      void ReportDuplicate();
      void ReportInvalid();
    }
  }
}
