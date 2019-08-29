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

    Task<byte[]> GetHeadersAsync(
      IEnumerable<byte[]> locatorHashes, 
      CancellationToken cancellationToken);
    
    Task RequestBlocksAsync(IEnumerable<byte[]> headerHashes);
    Task<byte[]> ReceiveBlockAsync(CancellationToken cancellationToken);

    string GetIdentification();
  }
}
