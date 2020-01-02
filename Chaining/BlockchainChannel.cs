using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

using BToken.Networking;



namespace BToken.Chaining
{
  partial class Blockchain
  {
    public class BlockchainChannel
    {
      Network.INetworkChannel NetworkChannel;

      const int TIMEOUT_GETHEADERS_MILLISECONDS = 5000;



      public BlockchainChannel(Network.INetworkChannel networkChannel)
      {
        NetworkChannel = networkChannel;
      }


      public async Task<Headerchain.HeaderContainer> GetHeaders(
        IEnumerable<byte[]> locatorHashes)
      {
        int timeout = TIMEOUT_GETHEADERS_MILLISECONDS;

        CancellationTokenSource cancellation = new CancellationTokenSource(timeout);
        await NetworkMessageStreamer.WriteAsync(
          new GetHeadersMessage(
            locatorHashes,
            ProtocolVersion));

        while (true)
        {
          NetworkMessage networkMessage = await ApplicationMessages
            .ReceiveAsync(cancellation.Token);

          if (networkMessage.Command == "headers")
          {
            var headerContainer = new Headerchain.HeaderContainer(
              networkMessage.Payload);

            headerContainer.Parse();

            return headerContainer;
          }
        }
      }


      public void ReportDuplicate()
      {
        throw new NotImplementedException();
      }
      public void ReportInvalid()
      {
        throw new NotImplementedException();
      }

      public string GetIdentification()
      {
        return NetworkChannel.GetIdentification();
      }

      public void Dispose()
      {
        NetworkChannel.Dispose();
      }
    }
  }
}
