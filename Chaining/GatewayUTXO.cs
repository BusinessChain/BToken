using System;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Linq;
using System.IO;
using System.Threading.Tasks;

using BToken.Networking;



namespace BToken.Chaining
{
  partial class UTXOTable
  {
    partial class GatewayUTXO : AbstractGateway
    {
      Network Network;
      UTXOTable UTXOTable;

      const int COUNT_UTXO_SESSIONS = 4;



      public GatewayUTXO(
        Network network,
        UTXOTable uTXOTable)
        : base(COUNT_UTXO_SESSIONS)
      {
        Network = network;
        UTXOTable = uTXOTable;
      }



      protected override Task CreateSyncSessionTask()
      {
        return new SyncUTXOSession(this).Start();
      }



      readonly object LOCK_BatchLoadedLast = new object();
      ConcurrentQueue<DataBatch> QueueBatchesCanceled
        = new ConcurrentQueue<DataBatch>();

      bool TryGetBatch(
        out DataBatch uTXOBatch,
        int countHeaders)
      {
        if (QueueBatchesCanceled.TryDequeue(out uTXOBatch))
        {
          return true;
        }

        lock (LOCK_BatchLoadedLast)
        {
          if (UTXOTable.TryLoadBatch(
            ContainerInsertedLast,
            out uTXOBatch,
            countHeaders))
          {
            ContainerInsertedLast
              = uTXOBatch.ItemBatchContainers.Last();

            return true;
          }
        }

        return false;
      }


      protected override void LoadImage(out int archiveIndexNext)
      {
        UTXOTable.LoadImage(out archiveIndexNext);
      }
      protected override bool TryInsertContainer(ItemBatchContainer container)
      {
        return UTXOTable.TryInsertContainer(
          (BlockBatchContainer)container);
      }

      protected override bool TryInsertBatch(
        DataBatch uTXOBatch,
        out ItemBatchContainer containerInvalid)
      {
        return UTXOTable.TryInsertBatch(
          uTXOBatch,
          out containerInvalid);
      }


      
      protected override void ArchiveBatch(DataBatch batch)
      {
        UTXOTable.ArchiveBatch(batch);
      }

      protected override ItemBatchContainer LoadDataContainer(
        int containerIndex)
      {
        return UTXOTable.LoadDataContainer(containerIndex);
      }

      async Task<UTXOChannel> RequestChannel()
      {
        INetworkChannel channel = await Network.RequestChannel();
        return new UTXOChannel(channel);
      }

      void ReturnChannel(UTXOChannel channel)
      {
        Network.ReturnChannel(
          channel.NetworkChannel);
      }

      void DisposeChannel(UTXOChannel channel)
      {
        Network.DisposeChannel(channel.NetworkChannel);
      }

           
      protected override async Task StartListener()
      {

      }
    }
  }
}
