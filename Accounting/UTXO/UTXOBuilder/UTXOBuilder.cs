
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

      Dictionary<byte[], List<TXInput>> InputsUnfunded;

      UTXOBuilderBatchMerger Merger;

      int COUNT_BUILD_TASKS_MAX = 5;
      List<Task> BuildTasks;

      int BLOCK_HEIGHT_START = 10000;
      int BATCH_COUNT = 100;


      public UTXOBuilder(UTXO uTXO, Headerchain.HeaderStream headerStreamer)
      {
        UTXO = uTXO;
        HeaderStreamer = headerStreamer;

        Merger = new UTXOBuilderBatchMerger(UTXO, this);

        InputsUnfunded = new Dictionary<byte[], List<TXInput>>(new EqualityComparerByteArray());
        BuildTasks = new List<Task>();
      }

      public async Task BuildAsync()
      {
        try
        {
          // Go To Block (debug)
          while (HeaderStreamer.TryReadHeader(out NetworkHeader header, out HeaderLocation location)
            && location.Height > BLOCK_HEIGHT_START) { }

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

            batchIndex++;
            headerLocations = GetHeaderLocationBatch();
          }

          await Task.WhenAll(BuildTasks);
          Console.WriteLine("UTXO build complete");
        }
        catch (Exception ex)
        {
          Console.WriteLine(ex.Message);
        }
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
      async Task AwaitNextBuildTaskASync()
      {
        if (BuildTasks.Count < COUNT_BUILD_TASKS_MAX)
        {
          return;
        }

        BuildTasks.Remove(await Task.WhenAny(BuildTasks));
      }

      async Task MergeBatchAsync(UTXOBuilderBatch uTXOBuilderBatch)
      {
        await Merger.MergeBatchAsync(uTXOBuilderBatch);
      }
    }
  }
}
