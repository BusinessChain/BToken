using System.Diagnostics;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Threading;

using BToken.Networking;


namespace BToken.Chaining
{
  public partial class Blockchain
  {
    partial class Headerchain
    {
      partial class HeaderchainController
      {
        class SessionHeaderDownload : INetworkSession
        {
          Headerchain Headerchain;
          IHeaderArchiver Archiver;

          INetworkChannel Channel;

          const double SECONDS_TIMEOUT_GETHEADERS = 2;


          public SessionHeaderDownload(Headerchain blockchain, IHeaderArchiver archiver)
          {
            Headerchain = blockchain;
            Archiver = archiver;
          }

          public async Task RunAsync(INetworkChannel channel, CancellationToken cancellationToken)
          {
            Channel = channel;

            await DownloadHeadersAsync();
          }

          async Task DownloadHeadersAsync()
          {
            List<NetworkHeader> headers = await GetHeadersAsync(Headerchain.Locator.GetHeaderLocator());

            using (var archiveWriter = Archiver.GetWriter())
            {
              while (headers.Any())
              {
                await InsertHeadersAsync(headers, archiveWriter);

                headers = await GetHeadersAsync(Headerchain.Locator.GetHeaderLocator());
              }
            }
          }
          async Task<List<NetworkHeader>> GetHeadersAsync(List<UInt256> headerLocator)
          {
            uint protocolVersion = Headerchain.Controller.Network.GetProtocolVersion();
            await Channel.SendMessageAsync(new GetHeadersMessage(headerLocator, protocolVersion));

            HeadersMessage headersMessage = await ReceiveHeadersMessageAsync();
            return headersMessage.Headers;
          }
          async Task<HeadersMessage> ReceiveHeadersMessageAsync()
          {
            CancellationTokenSource CancellationGetHeaders = new CancellationTokenSource(TimeSpan.FromSeconds(SECONDS_TIMEOUT_GETHEADERS));

            try
            {
              while (true)
              {
                NetworkMessage networkMessage = await Channel.ReceiveMessageAsync(CancellationGetHeaders.Token);

                if (networkMessage.Command == "headers")
                {
                  return new HeadersMessage(networkMessage);
                }
              }
            }
            catch (OperationCanceledException ex)
            {
              Console.WriteLine("Timeout 'getheaders'");
              throw ex;
            }
          }

          async Task InsertHeadersAsync(List<NetworkHeader> headers, IHeaderWriter archiveWriter)
          {
            foreach (NetworkHeader header in headers)
            {
              try
              {
                await Headerchain.InsertHeaderAsync(header);
              }
              catch (ChainException ex)
              {
                switch (ex.ErrorCode)
                {
                  case BlockCode.ORPHAN:
                    //await ProcessOrphanSessionAsync(headerHash);
                    return;

                  case BlockCode.DUPLICATE:
                    return;

                  default:
                    throw ex;
                }
              }

              archiveWriter.StoreHeader(header);
            }
          }

        }
      }
    }
  }
}
