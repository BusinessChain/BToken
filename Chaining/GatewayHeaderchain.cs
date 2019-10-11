using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
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
      Headerchain Headerchain;
      Network Network;

      readonly object LOCK_IsSyncing = new object();
      bool IsSyncing;
      bool IsSyncingCompleted;

      BufferBlock<Header> HeadersListened =
        new BufferBlock<Header>();

      const int COUNT_HEADER_SESSIONS = 4;



      public GatewayHeaderchain(
        Network network,
        Headerchain headerchain)
        : base(COUNT_HEADER_SESSIONS)
      {
        Network = network;
        Headerchain = headerchain;
      }

      

      protected override Task CreateSyncSessionTask()
      {
        return new SyncHeaderchainSession(this).Start();
      }



      IEnumerable<byte[]> LocatorHashes;
      int IndexHeaderBatch;
      DataBatch HeaderBatchOld;
      TaskCompletionSource<object> SignalStartHeaderSyncSession =
        new TaskCompletionSource<object>();

      DataBatch CreateHeaderBatch()
      {
        int batchIndex;
        IEnumerable<byte[]> locatorHashes;

        lock (LOCK_IsSyncing)
        {
          batchIndex = IndexHeaderBatch;

          if (LocatorHashes == null)
          {
            lock (Headerchain.LOCK_Chain)
            {
              LocatorHashes = Headerchain.Locator.GetHeaderHashes();
            }
          }

          locatorHashes = LocatorHashes;
        }

        var headerBatch = new DataBatch(batchIndex);

        headerBatch.ItemBatchContainers.Add(
          new HeaderBatchContainer(
            headerBatch,
            locatorHashes));

        return headerBatch;
      }



      public void ReportInvalidBatch(DataBatch batch)
      {
        Console.WriteLine("Invalid batch {0} reported",
          batch.Index);

        throw new NotImplementedException();
      }


      protected override void LoadImage(out int archiveIndexNext)
      {
        Headerchain.LoadImage(out archiveIndexNext);
      }

      protected override ItemBatchContainer LoadDataContainer(
        int containerIndex)
      {
        return Headerchain.LoadDataContainer(containerIndex);
      }


      protected override bool TryInsertContainer(ItemBatchContainer container)
      {
        return Headerchain.TryInsertContainer(
          (HeaderBatchContainer)container);
      }

      protected override bool TryInsertBatch(
        DataBatch uTXOBatch,
        out ItemBatchContainer containerInvalid)
      {
        return Headerchain.TryInsertBatch(
          uTXOBatch,
          out containerInvalid);
      }

      protected override void ArchiveBatch(DataBatch batch)
      {
        Headerchain.ArchiveBatch(batch);
      }

      protected override async Task StartListener()
      {
        while (true)
        {
          INetworkChannel channel = await Network.AcceptChannelInboundRequestAsync();

          List<NetworkMessage> messages = channel.GetApplicationMessages();

          lock (LOCK_IsSyncing)
          {
            if (!IsSyncingCompleted)
            {
              Network.ReturnChannel(channel);

              continue;
            }
          }

          try
          {
            foreach (NetworkMessage message in messages)
            {
              switch (message.Command)
              {
                case "getheaders":
                  //var getHeadersMessage = new GetHeadersMessage(inboundMessage);
                  //var headers = Headerchain.GetHeaders(getHeadersMessage.HeaderLocator, getHeadersMessage.StopHash);
                  //await channel.SendMessageAsync(new HeadersMessage(headers));
                  break;

                case "inv":
                  var invMessage = new InvMessage(message);

                  if (invMessage.Inventories.First().Type.ToString() != "MSG_TX")
                  {
                    Console.WriteLine("inv message with {0} {1} from channel {2}",
                      invMessage.Inventories.Count,
                      invMessage.Inventories.First().Type.ToString(),
                      channel.GetIdentification());
                  }

                  break;

                case "headers":
                  var headersMessage = new HeadersMessage(message);

                  Console.WriteLine("header message from channel {0}",
                    channel.GetIdentification());

                  HeaderBatchContainer container =
                    new HeaderBatchContainer(
                      -1,
                      headersMessage.Payload);

                  container.Parse();

                  if (Headerchain.TryInsertContainer(container))
                  {
                    Console.WriteLine("Inserted header {0}",
                      container.HeaderRoot.HeaderHash.ToHexString());

                    HeadersListened.Post(container.HeaderRoot);
                  }
                  else
                  {
                    Console.WriteLine("Failed to insert header {0}",
                      container.HeaderRoot.HeaderHash.ToHexString());
                  }

                  break;

                default:
                  break;
              }
            }

            Network.ReturnChannel(channel);
          }
          catch (Exception ex)
          {
            Console.WriteLine("Serving inbound request of channel '{0}' ended in exception '{1}'",
              channel.GetIdentification(),
              ex.Message);

            Network.DisposeChannel(channel);
          }
        }
      }
    }
  }
}
