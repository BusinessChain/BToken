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
    partial class BlockchainController
    {
      partial class BlockchainSession
      {
        class HeadersSession
        {
          BlockchainSession BlockchainSession;

          List<BlockLocation> HeaderLocator;



          public HeadersSession(BlockchainSession blockchainSession)
          {
            BlockchainSession = blockchainSession;
          }

          public async Task StartAsync(Network.HeadersMessage headersMessage)
          {
            List<NetworkHeader> headers = headersMessage.Headers;

            await InsertHeadersAsync(headers);
          }

          async Task InsertHeadersAsync(List<NetworkHeader> headers)
          {
            do
            {
              foreach (NetworkHeader header in headers)
              {
                UInt256 headerHash = CalculateHash(header.getBytes());

                try
                {
                  BlockchainSession.Controller.Blockchain.insertHeader(header, headerHash);
                }
                catch (BlockchainException ex)
                {
                  switch (ex.ErrorCode)
                  {
                    case BlockCode.ORPHAN:
                      await ProcessOrphanSessionAsync(headerHash);
                      return;

                    case BlockCode.DUPLICATE:
                      return;

                    default:
                      throw ex;
                  }
                }
              }

              headers = await GetHeadersAsync();

            } while (headers.Any());
            
            await new BlockSession(BlockchainSession).StartAsync();
          }

          async Task ProcessOrphanSessionAsync(UInt256 headerHashOrphan)
          {
            List<NetworkHeader> headers = await GetHeadersAsync();
            
            uint countDuplicatesAccepted = GetCountDuplicatesAccepted(headers);
            
            do
            {
              foreach (NetworkHeader header in headers)
              {
                UInt256 headerHash = CalculateHash(header.getBytes());

                try
                {
                  BlockchainSession.Controller.Blockchain.insertHeader(header, headerHash);
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

              await GetHeadersAsync();

            } while (headers.Any());

            // was advertised oprhan provided?
          }
          uint GetCountDuplicatesAccepted(List<NetworkHeader> headers)
          {
            if(!headers.Any())
            {
              return 0;
            }

            int rootHeaderLocatorIndex = HeaderLocator.FindIndex(b => b.Hash.isEqual(headers.First().HashPrevious));

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

              if (headers.Any(h => h.HashPrevious.isEqual(nextHigherLocator.Hash)))
              {
                throw new NetworkException("Superfluous locator headers");
              }

              return nextHigherLocator.Height - rootLocator.Height - 1;
            }
          }

          async Task<List<NetworkHeader>> GetHeadersAsync()
          {
            HeaderLocator = BlockchainSession.Controller.Blockchain.GetHeaderLocator();
            await BlockchainSession.RequestHeadersAsync(HeaderLocator);

            CancellationToken cancellationToken = new CancellationTokenSource(TimeSpan.FromSeconds(2)).Token;
            Network.HeadersMessage headersMessage = await GetHeadersMessageAsync(cancellationToken);
            
            return headersMessage.Headers;
          }

          async Task<Network.HeadersMessage> GetHeadersMessageAsync(CancellationToken cancellationToken)
          {
            Network.HeadersMessage headersMessage = await BlockchainSession.GetNetworkMessageAsync(cancellationToken) as Network.HeadersMessage;

            return headersMessage ?? await GetHeadersMessageAsync(cancellationToken);
          }

        }
      }
    }
  }
}
