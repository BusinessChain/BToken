using System;
using System.Collections.Generic;
using System.Collections.Concurrent;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using BToken.Networking;

namespace BToken.Chaining
{
  public partial class Blockchain
  {
    partial class GatewayUTXO : IGateway
    {
      Blockchain Blockchain;
      Network Network;
      UTXOTable UTXOTable;

      readonly object LOCK_IsSyncing = new object();
      bool IsSyncingCompleted;



      public GatewayUTXO(
        Blockchain blockchain,
        Network network,
        UTXOTable uTXOTable)
      {
        Blockchain = blockchain;
        Network = network;
        UTXOTable = uTXOTable;
      }



      const int COUNT_UTXO_SESSIONS = 4;
      ItemBatchContainer ItemBatchContainerInsertedLast;

      public async Task Synchronize(ItemBatchContainer itemBatchContainerInsertedLast)
      {
        ItemBatchContainerInsertedLast = itemBatchContainerInsertedLast;

        Task[] syncUTXOTasks = new Task[COUNT_UTXO_SESSIONS];

        for (int i = 0; i < COUNT_UTXO_SESSIONS; i += 1)
        {
          syncUTXOTasks[i] = new SyncUTXOSession(this).Start();
        }

        await Task.WhenAll(syncUTXOTasks);

        await Task.Delay(3000);

        Console.WriteLine("UTXO synced to hight {0}",
          Blockchain.UTXO.BlockHeight);
      }



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

        return UTXOTable.TryLoadBatch(
          ItemBatchContainerInsertedLast,
          out uTXOBatch,
          countHeaders);
      }



      public void ReportInvalidBatch(DataBatch batch)
      {
        throw new NotImplementedException();
      }
    }
  }
}
