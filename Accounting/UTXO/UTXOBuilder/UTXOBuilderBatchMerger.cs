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
            
            //Console.WriteLine("Start merge batch '{0}', height '{1} - {2}'", 
            //  uTXOBuilderBatch.BatchIndex,
            //  uTXOBuilderBatch.HeaderLocations.First().Height,
            //  uTXOBuilderBatch.HeaderLocations.Last().Height);

            var uTXOShards = new Dictionary<byte[], byte[]>[CountUTXOShards];
            foreach (KeyValuePair<byte[], byte[]> uTXO in uTXOBuilderBatch.UTXOs)
            {
              if (UTXOBuilder.InputsUnfunded.TryGetValue(uTXO.Key, out List<int> inputs))
              {
                SpendOutputsBits(uTXO.Value, inputs);
                UTXOBuilder.InputsUnfunded.Remove(uTXO.Key);
              }

              if (!AreAllOutputBitsSpent(uTXO.Value))
              {
                try
                {
                  UTXO.UTXOs.Add(uTXO.Key, uTXO.Value);
                  InsertUTXOInShard(uTXO, uTXOShards);
                }
                catch (ArgumentException)
                {
                  Console.WriteLine("Ambiguous transaction '{0}' in batch '{1}'",
                    new SoapHexBinary(uTXO.Key), uTXOBuilderBatch.BatchIndex);
                }
              }
            }

            foreach (KeyValuePair<byte[], List<int>> inputsBatch in uTXOBuilderBatch.InputsUnfunded)
            {
              if (UTXOBuilder.InputsUnfunded.TryGetValue(inputsBatch.Key, out List<int> outputIndexes))
              {
                outputIndexes.AddRange(inputsBatch.Value);
              }
              else
              {
                UTXOBuilder.InputsUnfunded.Add(inputsBatch.Key, inputsBatch.Value);
              }
            }

            await UTXOArchiver.ArchiveUTXOShardsAsync(uTXOShards); 

            Console.WriteLine("{0};{1};{2}",
              uTXOBuilderBatch.BatchIndex,
              UTXOBuilder.InputsUnfunded.Count,
              UTXO.UTXOs.Count);
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
