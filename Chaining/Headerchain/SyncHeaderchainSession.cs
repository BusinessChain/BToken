using System;
using System.Collections.Generic;
using System.Linq;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using System.Threading.Tasks.Dataflow;

using BToken.Networking;

namespace BToken.Chaining
{
  partial class Headerchain
  {
    partial class GatewayHeaderchain : AbstractGateway
    {
      class SyncHeaderchainSession
      {
        GatewayHeaderchain Gateway;

        public SyncHeaderchainSession(GatewayHeaderchain gateway)
        {
          Gateway = gateway;
        }



        INetworkChannel Channel;
        const int TIMEOUT_GETHEADERS_MILLISECONDS = 5000;
        DataBatch HeaderBatchOld;
        DataBatch HeaderBatch;
        bool IsSyncing;

        public async Task Start()
        {
          while (true)
          {
            Channel = await Gateway.Network.RequestChannel();

            try
            {
            StartRaceSyncHeaderSession:

              lock (Gateway.LOCK_IsSyncing)
              {
                if (Gateway.IsSyncingCompleted)
                {
                  Gateway.Network.ReturnChannel(Channel);
                  return;
                }
              }

              HeaderBatch = Gateway.CreateHeaderBatch();

              await DownloadHeaders();

              while (true)
              {
                lock (Gateway.LOCK_IsSyncing)
                {
                  if (Gateway.IsSyncingCompleted)
                  {
                    Gateway.Network.ReturnChannel(Channel);

                    return;
                  }

                  if (!Gateway.IsSyncing)
                  {
                    if (HeaderBatch.Index != Gateway.IndexHeaderBatch)
                    {
                      goto StartRaceSyncHeaderSession;
                    }
                    else
                    {
                      IsSyncing = true;
                      Gateway.IsSyncing = true;
                      break;
                    }
                  }
                }

                await Gateway.SignalStartHeaderSyncSession.Task.ConfigureAwait(false);
              }

              Console.WriteLine("session {0} enters header syncing", GetHashCode());

              Gateway.SignalStartHeaderSyncSession = new TaskCompletionSource<object>();
              HeaderBatchOld = Gateway.HeaderBatchOld;

              while (HeaderBatch.CountItems > 0)
              {
                if (HeaderBatchOld != null)
                {
                  await Gateway.InputBuffer.SendAsync(HeaderBatchOld);
                }

                HeaderBatchOld = HeaderBatch;

                HeaderBatch = CreateNextHeaderBatch();

                await DownloadHeaders();
              }

              if (HeaderBatchOld != null)
              {
                HeaderBatchOld.IsFinalBatch = true;

                await Gateway.InputBuffer.SendAsync(HeaderBatchOld);
              }

              lock (Gateway.LOCK_IsSyncing)
              {
                Gateway.IsSyncingCompleted = true;
              }

              Gateway.SignalStartHeaderSyncSession.SetResult(null);

              Console.WriteLine("chain session {0} returns {1}.",
                GetHashCode(),
                Channel.GetIdentification());

              Gateway.Network.ReturnChannel(Channel);

              return;
            }
            catch (Exception ex)
            {
              Console.WriteLine("Exception in SyncHeaderchainSession {0} with channel {1}: '{2}'",
                GetHashCode(),
                Channel == null ? "'null'" : Channel.GetIdentification(),
                ex.Message);

              Gateway.Network.DisposeChannel(Channel);

              lock (Gateway.LOCK_IsSyncing)
              {
                if (Gateway.IsSyncingCompleted)
                {
                  return;
                }
              }

              if (IsSyncing)
              {
                lock (Gateway.LOCK_IsSyncing)
                {
                  Gateway.IndexHeaderBatch = HeaderBatch.Index;
                  Gateway.LocatorHashes = ((HeaderBatchContainer)HeaderBatch.ItemBatchContainers.First())
                    .LocatorHashes;

                  Gateway.HeaderBatchOld = HeaderBatchOld;

                  Gateway.IsSyncing = false;
                }

                Gateway.SignalStartHeaderSyncSession.SetResult(null);
              }
            }
          }
        }



        DataBatch CreateNextHeaderBatch()
        {
          DataBatch batch = new DataBatch(HeaderBatch.Index + 1);

          batch.ItemBatchContainers.Add(
            new HeaderBatchContainer(
              batch,
              new List<byte[]> {
                ((HeaderBatchContainer)HeaderBatch.ItemBatchContainers[0])
                .HeaderTip.HeaderHash }));

          return batch;
        }


                     
        async Task DownloadHeaders()
        {
          int timeout = TIMEOUT_GETHEADERS_MILLISECONDS;

          CancellationTokenSource cancellation = new CancellationTokenSource(timeout);

          foreach (HeaderBatchContainer headerBatchContainer
            in HeaderBatch.ItemBatchContainers)
          {
            headerBatchContainer.Buffer = await Channel.GetHeaders(
              headerBatchContainer.LocatorHashes,
              cancellation.Token);

            headerBatchContainer.Parse();

            HeaderBatch.CountItems += headerBatchContainer.CountItems;
          }

          HeaderBatch.IsValid = true;

          return;
        }
      }
    }
  }
}
