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
      public class SessionHeaderDownload : INetworkSession
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

          await DownloadHeadersAsync();
        }

        async Task DownloadHeadersAsync()
        {
          List<NetworkHeader> headers = await GetHeadersAsync(Headerchain.GetHeaderLocator());

          using (var archiveWriter = Headerchain.Archiver.GetWriter())
          {
            while (headers.Any())
            {
              await InsertHeadersAsync(headers, archiveWriter);

              headers = await GetHeadersAsync(Headerchain.GetHeaderLocator());
            }
          }
        }
        async Task<List<NetworkHeader>> GetHeadersAsync(List<UInt256> headerLocator)
        {
          await Channel.SendMessageAsync(new GetHeadersMessage(headerLocator, Channel.GetProtocolVersion()));

          CancellationTokenSource CancellationGetHeaders = new CancellationTokenSource(TimeSpan.FromSeconds(SECONDS_TIMEOUT_GETHEADERS));
          return (await ReceiveHeadersMessageAsync(CancellationGetHeaders.Token)).Headers;
        }
        async Task<HeadersMessage> ReceiveHeadersMessageAsync(CancellationToken cancellationToken)
        {
          try
          {
            while (true)
            {
              NetworkMessage networkMessage = await Channel.ReceiveMessageAsync(cancellationToken);

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
