using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Threading.Tasks.Dataflow;
using System.Security.Cryptography;

using BToken.Networking;

namespace BToken.Chaining
{
  public partial class Blockchain
  {
    partial class GatewayBlockchainNetwork
    {
      class SyncHeaderchainSession
      {
        GatewayBlockchainNetwork Gateway;

        public SyncHeaderchainSession(GatewayBlockchainNetwork gateway)
        {
          Gateway = gateway;
        }



        INetworkChannel Channel;
        bool IsSyncing;
        const int TIMEOUT_GETHEADERS_MILLISECONDS = 10000;
        HeaderBatch HeaderBatchOld;
        HeaderBatch HeaderBatch;
        int HeaderBatchIndex;

        public async Task Start()
        {
          while (true)
          {
            try
            {
              Channel = await Gateway.Network.RequestChannelAsync();

              IEnumerable<byte[]> locatorHashes = Gateway.GetLocatorHashes();

              CancellationTokenSource cancellation =
                new CancellationTokenSource(TIMEOUT_GETHEADERS_MILLISECONDS);
              
              byte[] headerBytes = await Channel.GetHeadersAsync(
                locatorHashes,
                cancellation.Token);

              List<Header> headers = ParseHeaders(headerBytes);

              HeaderBatch = new HeaderBatch(
                headers,
                headerBytes,
                HeaderBatchIndex++);

              while (true)
              {
                lock (Gateway.LOCK_IsSyncing)
                {
                  if (Gateway.IsSyncingCompleted)
                  {
                    return;
                  }

                  if (!Gateway.IsSyncing)
                  {
                    Gateway.IsSyncing = true;
                    IsSyncing = true;
                    break;
                  }
                }

                await Task.Delay(3000);
              }

              Console.WriteLine("session {0} enters header syncing", GetHashCode());

              while (headers.Any())
              {
                if (HeaderBatchOld != null)
                {
                  await Gateway.Blockchain.HeaderchainDataPipe.InputBuffer.SendAsync(HeaderBatchOld);
                }

                HeaderBatchOld = HeaderBatch;

                locatorHashes = Gateway.SetLocatorHashes(
                  new List<byte[]> { headers.Last().HeaderHash });

                cancellation.CancelAfter(TIMEOUT_GETHEADERS_MILLISECONDS);
                headerBytes = await Channel.GetHeadersAsync(
                  locatorHashes,
                  cancellation.Token);

                headers = ParseHeaders(headerBytes);

                HeaderBatch = new HeaderBatch(
                    headers,
                    headerBytes,
                    HeaderBatchIndex++);
              }

              if (HeaderBatchOld != null)
              {
                HeaderBatchOld.IsLastBatch = true;
                await Gateway.Blockchain.HeaderchainDataPipe.InputBuffer
                  .SendAsync(HeaderBatchOld);
              }

              Console.WriteLine("session {0} completes chain syncing", GetHashCode());

              lock (Gateway.LOCK_IsSyncing)
              {
                Gateway.IsSyncingCompleted = true;
              }

              Console.WriteLine("session {0} ends chain syncing.", GetHashCode());

              return;
            }
            catch (Exception ex)
            {
              Console.WriteLine("Exception in chain syncing: '{0}' in session {1} with channel {2}",
                ex.Message,
                GetHashCode(),
                Channel == null ? "" : Channel.GetIdentification());

              lock (Gateway.LOCK_IsSyncing)
              {
                if (IsSyncing)
                {
                  IsSyncing = false;
                  Gateway.IsSyncing = false;
                }

                if (!Gateway.IsSyncingCompleted)
                {
                  continue;
                }
              }
            }

          }
        }



        SHA256 SHA256 = SHA256.Create();

        List<Header> ParseHeaders(byte[] headersBytes)
        {
          int startIndex = 0;
          var headers = new List<Header>();

          int headersCount = VarInt.GetInt32(headersBytes, ref startIndex);
          for (int i = 0; i < headersCount; i += 1)
          {
            headers.Add(Header.ParseHeader(headersBytes, ref startIndex, SHA256));
            startIndex += 1; // skip txCount (always a zero-byte)
          }

          return headers;
        }
      }
    }
  }
}
