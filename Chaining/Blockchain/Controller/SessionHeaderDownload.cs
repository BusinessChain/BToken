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
  partial class BlockchainController
  {
    class SessionHeaderDownload : BlockchainSession
    {
      Blockchain Blockchain;
      BlockchainController Controller;
      List<BlockLocation> HeaderLocator;
      List<NetworkHeader> Headers = new List<NetworkHeader>();


      public SessionHeaderDownload(BlockchainController controller, Blockchain blockchain)
      {
        Controller = controller;
        Blockchain = blockchain;
      }

      public override async Task StartAsync(BlockchainChannel channel)
      {
        Channel = channel;

        await DownloadHeadersAsync().ConfigureAwait(false);
      }

      async Task DownloadHeadersAsync()
      {
        await ReceiveHeaders().ConfigureAwait(false);

        while (Headers.Any())
        {
          InsertHeaders();
          
          await ReceiveHeaders().ConfigureAwait(false);
        }
      }
      async Task ReceiveHeaders() => Headers = await GetHeadersAsync();

      void InsertHeaders()
      {
        foreach (NetworkHeader header in Headers)
        {
          UInt256 headerHash = new UInt256(Hashing.SHA256d(header.getBytes()));

          try
          {
            Blockchain.InsertHeader(header, headerHash);
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
            UInt256 headerHash = new UInt256(Hashing.SHA256d(header.getBytes()));

            try
            {
              Blockchain.InsertHeader(header, headerHash);
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
        return await GetHeadersMessageAsync(cancellationToken).ConfigureAwait(false);
      }

      async Task<List<NetworkHeader>> GetHeadersMessageAsync(CancellationToken cancellationToken)
      {
        while(true)
        {
          NetworkMessage networkMessage = await Channel.GetNetworkMessageAsync(cancellationToken).ConfigureAwait(false);
          Network.HeadersMessage headersMessage = networkMessage as Network.HeadersMessage;

          if (headersMessage != null)
          {
            return headersMessage.Headers;
          }
        }
      }

    }
  }
}
