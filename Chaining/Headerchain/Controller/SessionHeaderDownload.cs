using System.Diagnostics;

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

using BToken.Networking;


namespace BToken.Chaining
{
  partial class Headerchain
  {
    partial class HeaderchainController
    {
      class SessionHeaderDownload : INetworkSession
      {
        Headerchain Blockchain;
        IHeaderArchiver Archiver;

        INetworkChannel Channel;


        public SessionHeaderDownload(Headerchain blockchain, IHeaderArchiver archiver)
        {
          Blockchain = blockchain;
          Archiver = archiver;
        }

        public async Task StartAsync(INetworkChannel channel)
        {
          Channel = channel;

          await DownloadHeadersAsync().ConfigureAwait(false);
        }

        async Task DownloadHeadersAsync()
        {
          List<NetworkHeader> headers = await Channel.GetHeadersAsync(Blockchain.Locator.GetHeaderLocator());

          using (var archiveWriter = Archiver.GetWriter())
          {
            while (headers.Any())
            {
              InsertHeaders(headers, archiveWriter);

              headers = await Channel.GetHeadersAsync(Blockchain.Locator.GetHeaderLocator());
            }
          }
        }

        void InsertHeaders(List<NetworkHeader> headers, IHeaderWriter archiveWriter)
        {
          foreach (NetworkHeader header in headers)
          {
            try
            {
              Blockchain.InsertHeader(header);
            }
            catch (HeaderchainException ex)
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
