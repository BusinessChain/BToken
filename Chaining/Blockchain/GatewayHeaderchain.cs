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
  public partial class Blockchain
  {
    partial class GatewayHeaderchain : IGateway
    {
      Blockchain Blockchain;
      Headerchain Headerchain;
      Network Network;
      
      readonly object LOCK_IsSyncing = new object();
      bool IsSyncing;
      bool IsSyncingCompleted;



      public GatewayHeaderchain(
        Blockchain blockchain,
        Network network, 
        Headerchain headerchain)
      {
        Blockchain = blockchain;
        Headerchain = headerchain;
        Network = network;
      }
      


      const int COUNT_HEADER_SESSIONS = 4;
      ItemBatchContainer ContainerInsertedLast;

      public async Task Synchronize(ItemBatchContainer containerInsertedLast)
      {
        ContainerInsertedLast = containerInsertedLast;

        Task[] syncHeaderchainTasks = new Task[COUNT_HEADER_SESSIONS];

        for (int i = 0; i < COUNT_HEADER_SESSIONS; i += 1)
        {
          syncHeaderchainTasks[i] = 
            new SyncHeaderchainSession(this).Start();
        }

        await Task.WhenAll(syncHeaderchainTasks);

        await Task.Delay(3000);

        Console.WriteLine("Chain synced to hight {0}",
          Blockchain.Chain.GetHeight());
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
            LocatorHashes = Blockchain.GetLocatorHashes();
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


      public async Task StartListener()
      {
        while (true)
        {
          INetworkChannel channel = await Network.AcceptChannelInboundRequestAsync();
          
          List<NetworkMessage> messages = channel.GetApplicationMessages();

          lock(LOCK_IsSyncing)
          {
            if(!IsSyncingCompleted)
            {
              Console.WriteLine("Chain listener returns channel {0} as syncing not completed",
                channel.GetIdentification());

              Network.ReturnChannel(channel);

              continue;
            }
          }

          Console.WriteLine("Chain listener accepts channel {0}",
            channel.GetIdentification());

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
                  Console.WriteLine("inv message");
                  var invMessage = new InvMessage(message);

                  invMessage.Inventories.ForEach(i => 
                  {
                    Console.WriteLine("received inventory type {0}",
                      i.Type.ToString());
                  });

                  break;

                case "headers":
                  var headersMessage = new HeadersMessage(message);

                  HeaderBatchContainer container =
                    new HeaderBatchContainer(
                      -1,
                      headersMessage.Payload);

                  container.Parse();

                  if (Headerchain.TryInsertContainer(container))
                  {
                    Console.WriteLine("Inserted header {0}",
                      container.HeaderRoot.HeaderHash.ToHexString());
                  }
                  else
                  {
                    Console.WriteLine("Failed to insert header {0}",
                      container.HeaderRoot.HeaderHash.ToHexString());
                  }


                  //await UTXO.NotifyBlockHeadersAsync(headersInserted, channel);
                  break;

                default:
                  break;
              }
            }

            Console.WriteLine("Chain listener returns channel {0}",
              channel.GetIdentification());

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
