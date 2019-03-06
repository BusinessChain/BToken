
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Runtime.Remoting.Metadata.W3cXsd2001;

using BToken.Chaining;
using BToken.Networking;

namespace BToken.Accounting
{
  public partial class UTXO
  {
    partial class UTXOBuilder
    {
      UTXO UTXO;
      Headerchain.HeaderStream HeaderStreamer;

      struct InputsCatcheItem
      {
        public int[] Value;
        public byte CountDuplicates;
      }
      Dictionary<int, InputsCatcheItem> PrimaryInputsCache;
      Dictionary<byte[], int[]> SecondaryInputsCache;

      UTXOBuilderBatchMerger Merger;

      int COUNT_BUILD_TASKS_MAX = 4;
      List<Task> BuildTasks = new List<Task>();

      int BATCH_COUNT = 30;


      public UTXOBuilder(UTXO uTXO, Headerchain.HeaderStream headerStreamer)
      {
        UTXO = uTXO;
        HeaderStreamer = headerStreamer;

        Merger = new UTXOBuilderBatchMerger(UTXO, this);

        PrimaryInputsCache = new Dictionary<int, InputsCatcheItem>();
        SecondaryInputsCache = new Dictionary<byte[], int[]>(new EqualityComparerByteArray());
      }

      public async Task BuildAsync()
      {
        try
        {
          List<HeaderLocation> headerLocations = GetHeaderLocationBatch();
          int batchIndex = 0;

          while (headerLocations.Any())
          {
            await AwaitNextBuildTaskASync();

            var uTXOBuilderBatch = new UTXOBuilderBatch(
              UTXO, 
              this, 
              headerLocations,
              batchIndex);

            BuildTasks.Add(uTXOBuilderBatch.BuildAsync());

            headerLocations = GetHeaderLocationBatch();
            batchIndex++;
          }

          await Task.WhenAll(BuildTasks);
          Console.WriteLine("UTXO build complete");
        }
        catch (Exception ex)
        {
          Console.WriteLine(ex.Message);
        }
      }
      async Task AwaitNextBuildTaskASync()
      {
        if (BuildTasks.Count < COUNT_BUILD_TASKS_MAX)
        {
          return;
        }

        Task buildTaskCompleted = await Task.WhenAny(BuildTasks);

        BuildTasks.Remove(buildTaskCompleted);
      }
      List<HeaderLocation> GetHeaderLocationBatch()
      {
        var headerLocations = new List<HeaderLocation>();

        while (headerLocations.Count < BATCH_COUNT)
        {
          if(!HeaderStreamer.TryReadHeader(out NetworkHeader header, out HeaderLocation chainLocation))
          {
            break;
          }

          headerLocations.Add(chainLocation);
        }

        return headerLocations;
      }

      public async Task MergeBatchAsync(UTXOBuilderBatch uTXOBuilderBatch)
      {
        await Merger.MergeBatchAsync(uTXOBuilderBatch);
      }

      public void WriteInputUnfunded(byte[] key, int[] value)
      {
        SecondaryInputsCache.Add(key, value);



        //  var item = new InputsCatcheItem
        //  {
        //    Value = value,
        //    CountDuplicates = 0,
        //  };

        //  int primaryKey = BitConverter.ToInt32(key, 0);
        //  if (PrimaryInputsCache.TryGetValue(primaryKey, out InputsCatcheItem itemExisting))
        //  {
        //    itemExisting.CountDuplicates++;
        //    SecondaryInputsCache.Add(key, value);
        //  }
        //  else
        //  {
        //    PrimaryInputsCache.Add(primaryKey, item);
        //  }
      }
      public bool TryGetInputUnfunded(byte[] key, out int[] value)
      {
        if (SecondaryInputsCache.TryGetValue(key, out value))
        {
          return true;
        }



        //int primaryKey = BitConverter.ToInt32(key, 0);

        //if (PrimaryInputsCache.TryGetValue(primaryKey, out InputsCatcheItem itemExisting))
        //{
        //  if (itemExisting.CountDuplicates > 0)
        //  {
        //    if (SecondaryInputsCache.TryGetValue(key, out value))
        //    {
        //      return true;
        //    }
        //  }

        //  value = itemExisting.Value;
        //  return true;
        //}

        value = null;
        return false;
      }
      public void RemoveInput(byte[] key)
      {
        SecondaryInputsCache.Remove(key);



        //int primaryKey = BitConverter.ToInt32(key, 0);

        //if (PrimaryInputsCache.TryGetValue(primaryKey, out InputsCatcheItem itemExisting))
        //{
        //  if (itemExisting.CountDuplicates > 0)
        //  {
        //    if (SecondaryInputsCache.Remove(key))
        //    {
        //      return;
        //    }
        //  }

        //  PrimaryInputsCache.Remove(primaryKey);
        //}

      }
    }
  }
}
