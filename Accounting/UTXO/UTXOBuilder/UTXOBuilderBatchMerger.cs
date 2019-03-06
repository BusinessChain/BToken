using System.Diagnostics;

using System;
using System.Linq;
using System.Collections.Generic;
using System.Runtime.Remoting.Metadata.W3cXsd2001;
using System.Threading.Tasks;
using System.Threading.Tasks.Dataflow;

namespace BToken.Accounting
{
  public partial class UTXO
  {
    partial class UTXOBuilder
    {
      class UTXOBuilderBatchMerger
      {
        UTXO UTXO;
        UTXOBuilder UTXOBuilder;

        readonly object DispatchLOCK = new object();
        BufferBlock<int> SignalMergerAvailableForBatchIndex = new BufferBlock<int>();
        bool IsDispatched = false;

        const int CountUTXOShards = 16;
        

        public UTXOBuilderBatchMerger(UTXO uTXO, UTXOBuilder uTXOBuilder)
        {
          UTXO = uTXO;
          UTXOBuilder = uTXOBuilder;
          SignalMergerAvailableForBatchIndex.Post(0);
        }

        public async Task MergeBatchAsync(UTXOBuilderBatch uTXOBuilderBatch)
        {
          try
          {
            await DispatchAsync(uTXOBuilderBatch.BatchIndex);

            var uTXOShards = new Dictionary<byte[], byte[]>[CountUTXOShards];
            foreach (KeyValuePair<byte[], byte[]> uTXO in uTXOBuilderBatch.UTXOs)
            {
              if (UTXOBuilder.TryGetInputUnfunded(uTXO.Key, out int[] inputs))
              {
                SpendOutputsBits(uTXO.Value, inputs);
                UTXOBuilder.RemoveInput(uTXO.Key);
              }

              if (!AreAllOutputBitsSpent(uTXO.Value))
              {
                try
                {
                  UTXO.Write(uTXO.Key, uTXO.Value);
                  InsertUTXOInShard(uTXO, uTXOShards);
                }
                catch (ArgumentException)
                {
                  Console.WriteLine("Ambiguous transaction '{0}' in batch '{1}'",
                    new SoapHexBinary(uTXO.Key), uTXOBuilderBatch.BatchIndex);
                }
              }
            }

            foreach (KeyValuePair<byte[], int[]> inputsBatch in uTXOBuilderBatch.InputsUnfunded)
            {
              if (UTXOBuilder.TryGetInputUnfunded(inputsBatch.Key, out int[] outputIndexes))
              {
                int[] temp = new int[outputIndexes.Length + inputsBatch.Value.Length];
                outputIndexes.CopyTo(temp, 0);
                inputsBatch.Value.CopyTo(temp, outputIndexes.Length);

                outputIndexes = temp;
              }
              else
              {
                UTXOBuilder.WriteInputUnfunded(inputsBatch.Key, inputsBatch.Value);
              }
            }

            await UTXOArchiver.ArchiveUTXOShardsAsync(uTXOShards); 

            Console.WriteLine("{0};{1};{2};{3};{4}",
              uTXOBuilderBatch.BatchIndex,
              UTXOBuilder.PrimaryInputsCache.Count,
              UTXOBuilder.SecondaryInputsCache.Count,
              UTXO.PrimaryCache.Count,
              UTXO.SecondaryCache.Count);
          }
          finally
          {
            lock (DispatchLOCK)
            {
              IsDispatched = false;
              int nextBatchToMerge = uTXOBuilderBatch.BatchIndex + 1;
              SignalMergerAvailableForBatchIndex.Post(nextBatchToMerge);
            }
          }
        }
        void InsertUTXOInShard(
          KeyValuePair<byte[], byte[]> uTXO, 
          Dictionary<byte[], byte[]>[] uTXOShards)
        {
          int shardIndex = uTXO.Key[1] % CountUTXOShards;

          if(uTXOShards[shardIndex] == null)
          {
            uTXOShards[shardIndex] = new Dictionary<byte[], byte[]>(new EqualityComparerByteArray());
          }

          uTXOShards[shardIndex].Add(uTXO.Key, uTXO.Value);
        }


        async Task DispatchAsync(int batchIndex)
        {
          while(true)
          {
            int nextBatchToMerge = await SignalMergerAvailableForBatchIndex.ReceiveAsync();

            if (batchIndex == nextBatchToMerge)
            {
              break;
            }

            SignalMergerAvailableForBatchIndex.Post(nextBatchToMerge);
            await Task.Delay(TimeSpan.FromSeconds(3));
          }

          if (!TryDispatch())
          {
            throw new UTXOException("Received signal available but could not dispatch UTXO merger.");
          }
        }

        public bool TryDispatch()
        {
          lock (DispatchLOCK)
          {
            if (IsDispatched)
            {
              return false;
            }

            IsDispatched = true;
            return true;
          }
        }

      }
    }
  }
}
