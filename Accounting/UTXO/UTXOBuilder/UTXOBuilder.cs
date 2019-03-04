
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

      int COUNT_BUILD_TASKS_MAX = 1;
      List<Task> BuildTasks;

      int BLOCK_HEIGHT_START = 50000;
      int BATCH_COUNT = 200;


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
          //while (HeaderStreamer.TryReadHeader(out NetworkHeader header, out HeaderLocation location)
          //  && location.Height > BLOCK_HEIGHT_START) { }

          List<HeaderLocation> headerLocations = GetHeaderLocationBatch();
          int batchIndex = 1;

          while (headerLocations.Any())
          {
            await AwaitNextBuildTaskASync();

            Console.WriteLine("Start build batch '{0}', headerLocation '{1}' - '{2}'", 
              batchIndex,
              headerLocations.First().Height,
              headerLocations.Last().Height);

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
    }
  }
}
