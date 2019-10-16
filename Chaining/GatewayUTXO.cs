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

        return UTXOTable.TryLoadBatch(
           out uTXOBatch,
           countHeaders);
      }


      protected override void LoadImage(out int archiveIndex)
      {
        UTXOTable.LoadImage();
        archiveIndex = UTXOTable.ArchiveIndex;
      }
      protected override bool TryInsertBatch(DataBatch batch)
      {
        return UTXOTable.TryInsertBatch(batch);
      }
      protected override bool TryInsertContainer(
        DataBatchContainer container)
      {
        BlockBatchContainer blockContainer = 
          (BlockBatchContainer)container;

        if (blockContainer.HeaderPrevious != UTXOTable.Header)
        {
          Console.WriteLine("HeaderPrevious {0} of batch {1} not equal to \nHeaderMergedLast {2}",
            blockContainer.HeaderPrevious.HeaderHash.ToHexString(),
            blockContainer.Index,
            UTXOTable.Header.HeaderHash.ToHexString());

          return false;
        }

        try
        {
          UTXOTable.InsertContainer(blockContainer);
        }
        catch (ChainException ex)
        {
          Console.WriteLine(
            "Insertion of blockBatchContainer {0} raised ChainException:\n {1}.",
            container.Index,
            ex.Message);

          return false;
        }

        UTXOTable.Header = blockContainer.Header;
        UTXOTable.ArchiveIndex += 1;

        UTXOTable.ArchiveState();

        UTXOTable.LogInsertion(
          blockContainer.StopwatchParse.ElapsedTicks,
          container.Index);

        return true;
      }

      protected override DataBatchContainer LoadDataContainer(
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
