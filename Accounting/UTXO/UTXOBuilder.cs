using System.Diagnostics;

using System;
using System.Collections.Generic;
using System.Runtime.Remoting.Metadata.W3cXsd2001;
using System.Linq;
using System.Threading.Tasks;

using BToken.Chaining;

namespace BToken.Accounting
{
  public partial class UTXO
  {
    partial class UTXOBuilder
    {
      UTXO UTXO;
      Headerchain.HeaderStream HeaderStreamer;

      int BLOCK_BATCH_SIZE = 500;

      Stopwatch StopWatchBuild= new Stopwatch();
      Stopwatch StopWatchMergeBatch = new Stopwatch();
      Stopwatch StopWatchGetBlocks = new Stopwatch();

      public UTXOBuilder(UTXO uTXO, Headerchain.HeaderStream headerStreamer)
      {
        UTXO = uTXO;
        HeaderStreamer = headerStreamer;
      }
      
      public async Task BuildAsync()
      {
        StopWatchBuild.Start();

        Console.WriteLine(
          "BatchIndex," +
          "PrimaryCacheCompressed," +
          "SecondaryCacheCompressed," +
          "PrimaryCache," +
          "SecondaryCache," +
          "Merge time");

        int batchIndex = 0;
        HeaderLocation[] headerLocations = HeaderStreamer.GetHeaderLocations(BLOCK_BATCH_SIZE);
        
        while (headerLocations != null)
        {
          //if(batchIndex == 20)
          //{
          //  break;
          //}
          UInt256[] hashes = headerLocations.Select(h => h.Hash).ToArray();

          Console.WriteLine("Start loading blocks for batch '{0}'", batchIndex);
          StopWatchGetBlocks.Restart();

          Block[] blocks = await UTXO.GetBlocksAsync(hashes);

          StopWatchGetBlocks.Stop();
          Console.WriteLine("Finished loading blocks for batch '{0}', time: '{1}'",
            batchIndex,
            StopWatchGetBlocks.Elapsed);

          MergeBatches(blocks);

          headerLocations = HeaderStreamer.GetHeaderLocations(BLOCK_BATCH_SIZE);
          batchIndex++;
        }

        StopWatchBuild.Stop();
        Console.WriteLine("UTXO build complete, time: '{0}'", StopWatchBuild.Elapsed);
      }
      void MergeBatches(Block[] blocks)
      {
        StopWatchMergeBatch.Restart();

        for (int b = 0; b < BLOCK_BATCH_SIZE; b++)
        {
          List<TX> tXs = blocks[b].TXs;
          List<byte[]> tXHashes = blocks[b].TXHashes;
          byte[] headerHashBytes = blocks[b].HeaderHash.GetBytes();

          for (int t = 0; t < tXs.Count; t++)
          {
            // debug

            byte[] outputTXHash = new byte[tXHashes[t].Length];
            tXHashes[t].CopyTo(outputTXHash, 0);
            Array.Reverse(outputTXHash);
            if (new SoapHexBinary(outputTXHash).ToString() == "C02D4826DEE0F0A810E9DC3DB49A484CDF90832C56991F0EBA88418B80C7EC29")
            {
              byte[] inputTXHash = new byte[tXHashes[t].Length];
              tXHashes[t].CopyTo(inputTXHash, 0);
              Array.Reverse(inputTXHash);

              Console.WriteLine("Write outputs of TX '{0}' to UTXO",
                new SoapHexBinary(outputTXHash));
            }

            // end debug

            UTXO.InsertUTXO(tXHashes[t], headerHashBytes, tXs[t].Outputs.Count);
          }

          for (int t = 1; t < tXs.Count; t++)
          {
            for (int i = 0; i < tXs[t].Inputs.Count; i++)
            {
              try
              {
                // debug

                byte[] outputTXHash = new byte[tXHashes[t].Length];
                tXs[t].Inputs[i].TXIDOutput.CopyTo(outputTXHash, 0);
                Array.Reverse(outputTXHash);
                string outputTXHashString = new SoapHexBinary(outputTXHash).ToString();

                if (outputTXHashString == "C02D4826DEE0F0A810E9DC3DB49A484CDF90832C56991F0EBA88418B80C7EC29")
                {
                  byte[] inputTXHash = new byte[tXHashes[t].Length];
                  tXHashes[t].CopyTo(inputTXHash, 0);
                  Array.Reverse(inputTXHash);

                  Console.WriteLine("Input '{0}' in TX '{1}' \n attempts to spend " +
                    "output '{2}' in TX '{3}'.",
                    i,
                    new SoapHexBinary(inputTXHash),
                    tXs[t].Inputs[i].IndexOutput,
                    new SoapHexBinary(outputTXHash));
                }

                // end debug


                UTXO.SpendUTXO(
                  tXs[t].Inputs[i].TXIDOutput,
                  tXs[t].Inputs[i].IndexOutput);
              }
              catch (UTXOException ex)
              {
                byte[] inputTXHash = new byte[tXHashes[t].Length];
                tXHashes[t].CopyTo(inputTXHash, 0);
                Array.Reverse(inputTXHash);

                byte[] outputTXHash = new byte[tXHashes[t].Length];
                tXs[t].Inputs[i].TXIDOutput.CopyTo(outputTXHash, 0);
                Array.Reverse(outputTXHash);

                Console.WriteLine("Input '{0}' in TX '{1}' \n failed to spend " +
                  "output '{2}' in TX '{3}': \n'{4}'.",
                  i,
                  new SoapHexBinary(inputTXHash),
                  tXs[t].Inputs[i].IndexOutput,
                  new SoapHexBinary(outputTXHash),
                  ex.Message);
              }
            }
          }
        }

        StopWatchMergeBatch.Stop();

        Console.WriteLine("{0},{1},{2},{3},{4}",
          UTXO.GetCountPrimaryCacheItemsUInt32(),
          UTXO.GetCountSecondaryCacheItemsUInt32(),
          UTXO.GetCountPrimaryCacheItemsByteArray(),
          UTXO.GetCountSecondaryCacheItemsByteArray(),
          StopWatchMergeBatch.ElapsedMilliseconds);
      }
    }
  }
}
