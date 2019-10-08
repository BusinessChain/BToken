using System;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using BToken.Networking;

namespace BToken.Chaining
{
  partial class GatewayUTXO : IGateway
  {
    Network Network;
    UTXOTable UTXOTable;

    BatchDataPipe DataPipe;


    public GatewayUTXO(
      Network network,
      UTXOTable uTXOTable)
    {
      Network = network;
      UTXOTable = uTXOTable;

      DataPipe = new BatchDataPipe(UTXOTable, this);
    }



    public async Task Start()
    {
      await DataPipe.Start();
    }



    const int COUNT_UTXO_SESSIONS = 4;
    ItemBatchContainer ContainerInsertedLast;

    public async Task Synchronize(ItemBatchContainer containerInsertedLast)
    {
      ContainerInsertedLast = containerInsertedLast;

      Task[] syncUTXOTasks = new Task[COUNT_UTXO_SESSIONS];

      for (int i = 0; i < COUNT_UTXO_SESSIONS; i += 1)
      {
        syncUTXOTasks[i] = new SyncUTXOSession(this).Start();
      }

      await Task.WhenAll(syncUTXOTasks);

      await Task.Delay(3000);

      Console.WriteLine("UTXO synced to hight {0}",
        UTXOTable.BlockHeight);
    }



    readonly object LOCK_BatchLoadedLast = new object();
    ConcurrentQueue<DataBatch> QueueBatchesCanceled
      = new ConcurrentQueue<DataBatch>();

    bool TryGetDownloadBatch(
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



    public void ReportInvalidBatch(DataBatch batch)
    {
      Console.WriteLine("Invalid batch {0} reported",
        batch.Index);

      throw new NotImplementedException();
    }


    public async Task StartListener()
    {

    }
  }
}
