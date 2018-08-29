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

          enum SessionState { START, ORPHAN, END };
          SessionState State = SessionState.START;

          List<BlockLocation> BlockLocator;



          public HeadersSession(BlockchainSession blockchainSession)
          {
            BlockchainSession = blockchainSession;
          }

          public async Task StartAsync(HeadersMessage headersMessage)
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

            int rootBlockLocatorIndex = BlockLocator.FindIndex(b => b.Hash.isEqual(headers.First().HashPrevious));

            if (rootBlockLocatorIndex < 0)
            {
              throw new NetworkException("Headers do not link in locator");
            }
            if (rootBlockLocatorIndex == 0)
            {
              return 0;
            }
            else
            {
              BlockLocation rootLocator = BlockLocator[rootBlockLocatorIndex];
              BlockLocation nextHigherLocator = BlockLocator[rootBlockLocatorIndex - 1];

              if (headers.Any(h => h.HashPrevious.isEqual(nextHigherLocator.Hash)))
              {
                throw new NetworkException("Superfluous locator headers");
              }

              return nextHigherLocator.Height - rootLocator.Height - 1;
            }
          }

          async Task<List<NetworkHeader>> GetHeadersAsync()
          {
            BlockLocator = BlockchainSession.Controller.Blockchain.GetBlockLocator();
            await BlockchainSession.RequestHeadersAsync(BlockLocator);

            CancellationToken cancellationToken = new CancellationTokenSource(TimeSpan.FromSeconds(2)).Token;
            HeadersMessage headersMessage = await GetHeadersMessageAsync(cancellationToken);
            
            return headersMessage.Headers;
          }

          async Task<HeadersMessage> GetHeadersMessageAsync(CancellationToken cancellationToken)
          {
            HeadersMessage headersMessage = await BlockchainSession.GetNetworkMessageAsync(cancellationToken) as HeadersMessage;

            return headersMessage ?? await GetHeadersMessageAsync(cancellationToken);
          }

        }
      }
    }
  }
}
