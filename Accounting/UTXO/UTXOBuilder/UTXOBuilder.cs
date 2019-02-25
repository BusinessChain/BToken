
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
          

          while (headerLocations.Any())
          {
            await AwaitNextBuildTaskASync();

            var uTXOBuilderBatch = new UTXOBuilderBatch(UTXO, this, headerLocations);
            BuildTasks.Add(uTXOBuilderBatch.BuildAsync());
            
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

      static void SpendOutputBits(byte[] uTXO, List<TXInput> inputs)
      {
        for (int i = 0; i < inputs.Count; i++)
        {
          int byteIndex = inputs[i].IndexOutput / 8 + CountHeaderIndexBytes;
          int bitIndex = inputs[i].IndexOutput % 8;

          if((uTXO[byteIndex] & (byte)(0x01 << bitIndex)) != 0x00)
          {
            throw new UTXOException(string.Format("Output '{0}'-'{1}' already spent.",
              new SoapHexBinary(inputs[i].TXIDOutput), 
              inputs[i].IndexOutput));
          }
          uTXO[byteIndex] |= (byte)(0x01 << bitIndex);
        }
      }
      static bool AreAllOutputBitsSpent(byte[] uTXOIndex)
      {
        for (int i = CountHeaderIndexBytes; i < uTXOIndex.Length; i++)
        {
          if (uTXOIndex[i] != 0xFF) { return false; }
        }

        return true;
      }
    }
  }
}
