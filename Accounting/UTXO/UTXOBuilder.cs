
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

      Dictionary<byte[], int[]> InputsUnfunded;

      int BATCH_SIZE = 20;
      int COUNT_TASKS_MAX = 4;
      List<Task<UTXOBatch>> GetBlocksTasks = new List<Task<UTXOBatch>>();
      int NextBatchIndexToMerge = 0;
      List<UTXOBatch> BatchesAwaitingMerge = new List<UTXOBatch>();


      public UTXOBuilder(UTXO uTXO, Headerchain.HeaderStream headerStreamer)
      {
        UTXO = uTXO;
        HeaderStreamer = headerStreamer;
        InputsUnfunded = new Dictionary<byte[], int[]>(new EqualityComparerByteArray());
      }
      
      public async Task BuildAsync()
      {
        int batchIndex = 0;
        List<HeaderLocation> headerLocations = GetHeaderLocations(BATCH_SIZE);

        while (headerLocations.Any())
        {
          var batch = new UTXOBatch(batchIndex, headerLocations);
                   
          if (GetBlocksTasks.Count >= COUNT_TASKS_MAX)
          {
            await BuildBatchAsync();
          }

          GetBlocksTasks.Add(GetBlocksAsync(batch));
          
          headerLocations = GetHeaderLocations(BATCH_SIZE);
          batchIndex++;
        }

        while(GetBlocksTasks.Any())
        {
          await BuildBatchAsync();
        }

        Console.WriteLine("UTXO build complete");
      }
      async Task BuildBatchAsync()
      { 
        UTXOBatch batch = await AwaitNextBatchBlockTaskCompleted();

        while (batch != null)
        {
          if (batch.BatchIndex == NextBatchIndexToMerge)
          {
            foreach (Block block in batch.Blocks)
            {
              BuildBlock(block, batch.UTXOs);
            }

            Console.WriteLine("{0},{1},{2},{3}", 
              batch.BatchIndex,
              InputsUnfunded.Count,
              UTXO.PrimaryCache.Count,
              UTXO.SecondaryCache.Count);

            BatchesAwaitingMerge.Remove(batch);

            NextBatchIndexToMerge++;
            batch = BatchesAwaitingMerge.Find(b => b.BatchIndex == NextBatchIndexToMerge);
          }
          else
          {
            BatchesAwaitingMerge.Add(batch);
            batch = await AwaitNextBatchBlockTaskCompleted();
          }
        }
      }
      async Task<UTXOBatch> AwaitNextBatchBlockTaskCompleted()
      {
        Task<UTXOBatch> getBlockTaskCompleted = await Task.WhenAny(GetBlocksTasks);
        GetBlocksTasks.Remove(getBlockTaskCompleted);
        return await getBlockTaskCompleted;
      }
      List<HeaderLocation> GetHeaderLocations(int batchCount)
      {
        var headerLocations = new List<HeaderLocation>();

        while (headerLocations.Count < batchCount)
        {
          if (!HeaderStreamer.TryReadHeader(out NetworkHeader header, out HeaderLocation chainLocation))
          {
            break;
          }

          headerLocations.Add(chainLocation);
        }

        return headerLocations;
      }
      async Task<UTXOBatch> GetBlocksAsync(UTXOBatch batch)
      {
        try
        {
          foreach (HeaderLocation headerLocation in batch.HeaderLocations)
          {
            Block block = await UTXO.GetBlockAsync(headerLocation.Hash);
            batch.Blocks.Add(block);
          }

          return batch;
        }
        catch (UTXOException ex)
        {
          Console.WriteLine("Build batch '{0}' threw UTXOException: '{1}'",
            batch.BatchIndex, ex.Message);

          return null;
        }
        catch (Exception ex)
        {
          Console.WriteLine("Build batch '{0}' threw unexpected exception: '{1}'",
            batch.BatchIndex, ex.Message);

          return null;
        }
      }

      void BuildBlock(
        Block block,
        Dictionary<byte[], byte[]> uTXOs)
      {
        List<TX> tXs = block.TXs;
        List<byte[]> tXHashes = block.TXHashes;
        UInt256 headerHash = block.HeaderHash;

        for (int t = 1; t < tXs.Count; t++)
        {
          for (int i = 0; i < tXs[t].Inputs.Count; i++)
          {
            InsertInput(tXs[t].Inputs[i]);
          }
        }

        for (int t = 0; t < tXs.Count; t++)
        {
          try
          {
            byte[] uTXO = CreateUTXO(headerHash, tXs[t].Outputs.Count);

            if (InputsUnfunded.TryGetValue(tXHashes[t], out int[] outputIndexes))
            {
              SpendOutputs(uTXO, outputIndexes);
              InputsUnfunded.Remove(tXHashes[t]);
            }

            if (!AreAllOutputBitsSpent(uTXO))
            {
              UTXO.Write(new KeyValuePair<byte[], byte[]>(tXHashes[t], uTXO));
            }
          }
          catch (Exception ex)
          {
            Console.WriteLine("insert outputs of tx '{0}', index '{1}', threw exception: '{2}'",
              tXHashes[t],
              t,
              ex.Message);
          }
        }
      }

      void InsertInput(TXInput input)
      {
        if (InputsUnfunded.TryGetValue(input.TXIDOutput, out int[] outputIndexes))
        {
          for (int i = 0; i < outputIndexes.Length; i++)
          {
            if (outputIndexes[i] == input.IndexOutput)
            {
              throw new UTXOException(string.Format("Double spent output. TX = '{0}', index = '{1}'.",
                Bytes2HexStringReversed(input.TXIDOutput),
                input.IndexOutput));
            }
          }

          int[] temp = new int[outputIndexes.Length + 1];
          outputIndexes.CopyTo(temp, 0);
          temp[outputIndexes.Length] = input.IndexOutput;

          outputIndexes = temp;
        }
        else
        {
          InputsUnfunded.Add(input.TXIDOutput, new int[1] { input.IndexOutput });
        }
      }

    }
  }
}
