using System.Diagnostics;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks.Dataflow;
using System.Threading.Tasks;
using System.Threading;

using BToken.Networking;


namespace BToken.Chaining
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
        

        public SessionHeaderDownload(Headerchain blockchain, IHeaderArchiver archiver)
        {
          Headerchain = blockchain;
          Archiver = archiver;
        }

        public async Task RunAsync(INetworkChannel channel, CancellationToken cancellationToken)
        {
          Channel = channel;

          await DownloadHeadersAsync(cancellationToken);
        }

        async Task DownloadHeadersAsync(CancellationToken cancellationDownloadHeaders)
        {
          CancellationTokenSource CancellationGetHeaders = CancellationTokenSource.CreateLinkedTokenSource(cancellationDownloadHeaders);
          CancellationGetHeaders.CancelAfter(TimeSpan.FromSeconds(20));

          List<NetworkHeader> headers = await GetHeadersAsync(Headerchain.Locator.GetHeaderLocator(), CancellationGetHeaders.Token);

          using (var archiveWriter = Archiver.GetWriter())
          {
            while (headers.Any())
            {
              InsertHeaders(headers, archiveWriter);

              CancellationGetHeaders.CancelAfter(TimeSpan.FromSeconds(2));

              try
              {
                headers = await GetHeadersAsync(Headerchain.Locator.GetHeaderLocator(), CancellationGetHeaders.Token);
              }
              catch (OperationCanceledException ex)
              {
                Console.WriteLine("");
                throw ex;
              }
            }
          }
        }
        async Task<List<NetworkHeader>> GetHeadersAsync(List<UInt256> headerLocator, CancellationToken cancellationToken)
        {
          uint protocolVersion = Headerchain.Controller.Network.GetProtocolVersion();
          await Channel.SendMessageAsync(new GetHeadersMessage(headerLocator, protocolVersion));

          HeadersMessage headersMessage = await ReceiveHeadersMessageAsync(cancellationToken);
          return headersMessage.Headers;
        }
        async Task<HeadersMessage> ReceiveHeadersMessageAsync(CancellationToken cancellationToken)
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


        void InsertHeaders(List<NetworkHeader> headers, IHeaderWriter archiveWriter)
        {
          foreach (NetworkHeader header in headers)
          {
            try
            {
              Headerchain.InsertHeader(header);
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
