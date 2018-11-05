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
  public partial class Blockchain
  {
    partial class BlockchainController
    {
      class SessionHeaderDownload : BlockchainSession
      {
        Blockchain Blockchain;
        BlockchainController Controller;
        List<BlockLocation> HeaderLocator;
        List<NetworkHeader> Headers = new List<NetworkHeader>();

        BlockArchiver.FileWriter FileWriter;


        public SessionHeaderDownload(BlockchainController controller)
        {
          Controller = controller;
          Blockchain = controller.Blockchain;
          FileWriter = Controller.Blockchain.Archiver.GetWriter();
        }

        public override async Task StartAsync(BlockchainChannel channel)
        {
          Channel = channel;

          await DownloadHeadersAsync().ConfigureAwait(false);
        }

        async Task DownloadHeadersAsync()
        {
          using (var archiveWriter = new HeaderArchiver.HeaderWriter())
          {
            await ReceiveHeaders().ConfigureAwait(false);

            while (Headers.Any())
            {
              InsertHeaders(archiveWriter);

              await ReceiveHeaders().ConfigureAwait(false);
            }
          }
        }
        async Task ReceiveHeaders() => Headers = await GetHeadersAsync();

        void InsertHeaders(HeaderArchiver.HeaderWriter archiveWriter)
        {
          foreach (NetworkHeader header in Headers)
          {
            try
            {
              Blockchain.InsertHeader(header);
            }
            catch (BlockchainException ex)
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
        async Task ProcessOrphanSessionAsync(UInt256 headerHashOrphan)
        {
          List<NetworkHeader> headers = await GetHeadersAsync();

          uint countDuplicatesAccepted = GetCountDuplicatesAccepted(headers);

          do
          {
            foreach (NetworkHeader header in headers)
            {
              try
              {
                Blockchain.InsertHeader(header);
              }
              catch (BlockchainException ex)
              {
                switch (ex.ErrorCode)
                {
                  case BlockCode.DUPLICATE:
                    if (countDuplicatesAccepted-- > 0)
                    {
                      break;
                    }
                    return;

                  default:
                    throw ex;
                }
              }
            }

            headers = await GetHeadersAsync();

          } while (headers.Any());

          // should we check whether advertised oprhan was provided? I would say unnecessary
        }
        uint GetCountDuplicatesAccepted(List<NetworkHeader> headers)
        {
          if (!headers.Any())
          {
            return 0;
          }

          int rootHeaderLocatorIndex = HeaderLocator.FindIndex(b => b.Hash.IsEqual(headers.First().HashPrevious));

          if (rootHeaderLocatorIndex < 0)
          {
            throw new NetworkException("Headers do not link in locator");
          }
          if (rootHeaderLocatorIndex == 0)
          {
            return 0;
          }
          else
          {
            BlockLocation rootLocator = HeaderLocator[rootHeaderLocatorIndex];
            BlockLocation nextHigherLocator = HeaderLocator[rootHeaderLocatorIndex - 1];

            if (headers.Any(h => h.HashPrevious.IsEqual(nextHigherLocator.Hash)))
            {
              throw new NetworkException("Superfluous locator headers");
            }

            return nextHigherLocator.Height - rootLocator.Height - 1;
          }
        }

        async Task<List<NetworkHeader>> GetHeadersAsync()
        {
          HeaderLocator = Blockchain.GetBlockLocations();
          await Channel.RequestHeadersAsync(HeaderLocator).ConfigureAwait(false);

          CancellationToken cancellationToken = new CancellationTokenSource(TimeSpan.FromSeconds(2)).Token;
          Network.HeadersMessage headersMessage = await GetHeadersMessageAsync(cancellationToken).ConfigureAwait(false);
          return headersMessage.Headers;
        }

        async Task<Network.HeadersMessage> GetHeadersMessageAsync(CancellationToken cancellationToken)
        {
          while (true)
          {
            NetworkMessage networkMessage = await Channel.GetNetworkMessageAsync(cancellationToken).ConfigureAwait(false);
            Network.HeadersMessage headersMessage = networkMessage as Network.HeadersMessage;

            if (headersMessage != null)
            {
              return headersMessage;
            }
          }
        }

      }
    }
  }
}
