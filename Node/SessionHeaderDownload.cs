using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Threading;

using BToken.Networking;
using BToken.Chaining;


namespace BToken
{
  public partial class BitcoinNode
  {
    class SessionHeaderDownload : INetworkSession
    {
      Headerchain Headerchain;
      INetworkChannel Channel;

      const double SECONDS_TIMEOUT_GETHEADERS = 2;


      public SessionHeaderDownload(Headerchain headerchain)
      {
        Headerchain = headerchain;
      }

      public async Task RunAsync(INetworkChannel channel, CancellationToken cancellationToken)
      {
        Channel = channel;

        List<NetworkHeader> headers = await GetHeadersAsync(Headerchain.Locator.ToList());

        using (var archiveWriter = new Headerchain.HeaderWriter())
        {
          while (headers.Any())
          {
            await Headerchain.InsertHeadersAsync(archiveWriter, headers);
            headers = await GetHeadersAsync(Headerchain.Locator.ToList());
          }
        }
      }
      async Task<List<NetworkHeader>> GetHeadersAsync(List<UInt256> headerLocator)
      {
        await Channel.SendMessageAsync(new GetHeadersMessage(headerLocator, Channel.GetProtocolVersion()));

        CancellationTokenSource CancellationGetHeaders = new CancellationTokenSource(TimeSpan.FromSeconds(SECONDS_TIMEOUT_GETHEADERS));
        try
        {
          while (true)
          {
            NetworkMessage networkMessage = await Channel.ReceiveSessionMessageAsync(CancellationGetHeaders.Token);

            if (networkMessage.Command == "headers")
            {
              return new HeadersMessage(networkMessage).Headers;
            }
          }
        }
        catch (OperationCanceledException ex)
        {
          Console.WriteLine("Timeout 'getheaders'");
          throw ex;
        }
      }

      public string GetSessionID()
      {
        return "Header download session";
      }
    }
  }
}
