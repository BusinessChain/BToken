using System.Diagnostics;

using System;
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

        readonly object IsDispatchedLOCK = new object();
        bool IsDispatched = false;
        BufferBlock<bool> SignalMergerAvailable = new BufferBlock<bool>();

        int BatchIndex;

        const int CountUTXOShards = 16;
        

        public UTXOBuilderBatchMerger(UTXO uTXO, UTXOBuilder uTXOBuilder)
        {
          UTXO = uTXO;
          UTXOBuilder = uTXOBuilder;
          SignalMergerAvailable.Post(true);

          BatchIndex = 0;
        }

        public async Task MergeBatchAsync(UTXOBuilderBatch uTXOBuilderBatch)
        {
          try
          {
            await DispatchAsync();

            Console.WriteLine("Start merge batch '{0}'", uTXOBuilderBatch.BatchIndex);

            var uTXOShards = new Dictionary<byte[], byte[]>[CountUTXOShards];
            foreach (KeyValuePair<byte[], byte[]> uTXO in uTXOBuilderBatch.UTXOs)
            {
              if (UTXOBuilder.InputsUnfunded.TryGetValue(uTXO.Key, out List<TXInput> inputs))
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
                  Console.WriteLine("Ambiguous transaction '{0}' in batch",
                    new SoapHexBinary(uTXO.Key));
                }
              }
            }

            foreach (KeyValuePair<byte[], List<TXInput>> inputsBatch 
              in uTXOBuilderBatch.InputsUnfunded)
            {
              if (UTXO.UTXOs.TryGetValue(inputsBatch.Key, out byte[] uTXO))
              {
                SpendOutputsBits(uTXO, inputsBatch.Value);

                if(AreAllOutputBitsSpent(uTXO))
                {
                  UTXO.UTXOs.Remove(inputsBatch.Key);
                  await UTXOArchiver.DeleteUTXOAsync(inputsBatch.Key);
                }

                continue;
              }

              if (UTXOBuilder.InputsUnfunded.TryGetValue(inputsBatch.Key, out List<TXInput> inputs))
              {
                inputs.AddRange(inputsBatch.Value);
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
            lock (IsDispatchedLOCK)
            {
              IsDispatched = false;
              SignalMergerAvailable.Post(true);
            }
          }
        }
        void InsertUTXOInShard(
          KeyValuePair<byte[], byte[]> uTXO, 
          Dictionary<byte[], byte[]>[] uTXOShards)
        {
          int shardIndex = uTXO.Key[0] % CountUTXOShards;

          if(uTXOShards[shardIndex] == null)
          {
            uTXOShards[shardIndex] = new Dictionary<byte[], byte[]>(new EqualityComparerByteArray());
          }

          uTXOShards[shardIndex].Add(uTXO.Key, uTXO.Value);
        }


        async Task DispatchAsync()
        {
          await SignalMergerAvailable.ReceiveAsync();

          if (!TryDispatch())
          {
            throw new UTXOException("Received signal available but could not dispatch UTXO merger.");
          }
        }

        public bool TryDispatch()
        {
          lock (IsDispatchedLOCK)
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
