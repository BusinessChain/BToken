using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using BToken.Chaining;

namespace BToken.Accounting
{
  public partial class UTXO
  {
    partial class UTXOBuilder
    {
      class UTXOBatch
      {
        public int BatchIndex;
        public List<HeaderLocation> HeaderLocations;
        public List<Block> Blocks = new List<Block>();

        Dictionary<byte[], int[]> InputsUnfunded;
        public Dictionary<byte[], byte[]> UTXOs;


        public UTXOBatch(int batchIndex, List<HeaderLocation> headerLocations)
        {
          BatchIndex = batchIndex;
          HeaderLocations = headerLocations;

          InputsUnfunded = new Dictionary<byte[], int[]>(new EqualityComparerByteArray());
          UTXOs = new Dictionary<byte[], byte[]>(new EqualityComparerByteArray());
        }

        UTXOBatch(int batchIndex, Dictionary<byte[], byte[]> uTXOsBatch)
        {
          BatchIndex = batchIndex;
          UTXOs = uTXOsBatch; 
        }

        public static UTXOBatch BuildBatchAsync(
          UTXOBuilder uTXOBuilder,
          List<Block> blockLocations,
          int batchIndex)
        {
          var uTXOsBatch = new Dictionary<byte[], byte[]>(new EqualityComparerByteArray());

          try
          {
            foreach (Block block in blockLocations)
            {
              List<TX> tXs = block.TXs;
              List<byte[]> tXHashes = block.TXHashes;
              UInt256 headerHash = block.HeaderHash;

              // Insert tx inputs
              for (int t = 1; t < tXs.Count; t++)
              {
                for (int i = 0; i < tXs[t].Inputs.Count; i++)
                {
                  if (uTXOBuilder.InputsUnfunded.TryGetValue(tXs[t].Inputs[i].TXIDOutput, out int[] outputIndexes))
                  {
                    for (int o = 0; o < outputIndexes.Length; o++)
                    {
                      if (outputIndexes[o] == tXs[t].Inputs[i].IndexOutput)
                      {
                        throw new UTXOException(string.Format("Double spent output. TX = '{0}', index = '{1}'.",
                          Bytes2HexStringReversed(tXs[t].Inputs[i].TXIDOutput),
                          tXs[t].Inputs[i].IndexOutput));
                      }
                    }

                    int[] temp = new int[outputIndexes.Length + 1];
                    outputIndexes.CopyTo(temp, 0);
                    temp[outputIndexes.Length] = tXs[t].Inputs[i].IndexOutput;

                    outputIndexes = temp;
                  }
                  else
                  {
                    uTXOBuilder.InputsUnfunded.Add(tXs[t].Inputs[i].TXIDOutput, new int[1] { tXs[t].Inputs[i].IndexOutput });
                  }
                }
              }

              // insert tx outputs
              for (int t = 0; t < tXs.Count; t++)
              {
                byte[] uTXO = CreateUTXO(headerHash, tXs[t].Outputs.Count);

                if (uTXOBuilder.InputsUnfunded.TryGetValue(tXHashes[t], out int[] outputIndexes))
                {
                  SpendOutputs(uTXO, outputIndexes);
                  uTXOBuilder.InputsUnfunded.Remove(tXHashes[t]);
                }

                if (!AreAllOutputBitsSpent(uTXO))
                {
                  uTXOsBatch.Add(tXHashes[t], uTXO);
                }
              }
            }

            return new UTXOBatch(batchIndex, uTXOsBatch);
          }
          catch (UTXOException ex)
          {
            Console.WriteLine("Build batch '{0}' threw UTXOException: '{1}'", batchIndex, ex.Message);
            throw ex;
          }
          catch (Exception ex)
          {
            Console.WriteLine("Build batch '{0}' threw unexpected exception: '{1}'", batchIndex, ex.Message);
            throw ex;
          }
        }


        //static void BuildBlock(Block block)
        //{
        //  List<TX> tXs = block.TXs;
        //  List<byte[]> tXHashes = block.TXHashes;
        //  UInt256 headerHash = block.HeaderHash;

        //  // Insert tx inputs
        //  for (int t = 1; t < tXs.Count; t++)
        //  {
        //    for (int i = 0; i < tXs[t].Inputs.Count; i++)
        //    {
        //      if (UTXOBuilder.InputsUnfunded.TryGetValue(tXs[t].Inputs[i].TXIDOutput, out int[] outputIndexes))
        //      {
        //        for (int o = 0; o < outputIndexes.Length; o++)
        //        {
        //          if (outputIndexes[o] == tXs[t].Inputs[i].IndexOutput)
        //          {
        //            throw new UTXOException(string.Format("Double spent output. TX = '{0}', index = '{1}'.",
        //              Bytes2HexStringReversed(tXs[t].Inputs[i].TXIDOutput),
        //              tXs[t].Inputs[i].IndexOutput));
        //          }
        //        }

        //        int[] temp = new int[outputIndexes.Length + 1];
        //        outputIndexes.CopyTo(temp, 0);
        //        temp[outputIndexes.Length] = tXs[t].Inputs[i].IndexOutput;

        //        outputIndexes = temp;
        //      }
        //      else
        //      {
        //        UTXOBuilder.InputsUnfunded.Add(tXs[t].Inputs[i].TXIDOutput, new int[1] { tXs[t].Inputs[i].IndexOutput });
        //      }
        //    }
        //  }

        //  // insert tx outputs
        //  for (int t = 0; t < tXs.Count; t++)
        //  {
        //    byte[] uTXO = CreateUTXO(headerHash, tXs[t].Outputs.Count);

        //    if (UTXOBuilder.InputsUnfunded.TryGetValue(tXHashes[t], out int[] outputIndexes))
        //    {
        //      SpendOutputs(uTXO, outputIndexes);
        //      UTXOBuilder.InputsUnfunded.Remove(tXHashes[t]);
        //    }

        //    if (!AreAllOutputBitsSpent(uTXO))
        //    {
        //      UTXOsBatch.Add(tXHashes[t], uTXO);
        //    }
        //  }
        //}

        //static void InsertInput(TXInput input)
        //{
        //  if (UTXOBuilder.InputsUnfunded.TryGetValue(input.TXIDOutput, out int[] outputIndexes))
        //  {
        //    for (int i = 0; i < outputIndexes.Length; i++)
        //    {
        //      if (outputIndexes[i] == input.IndexOutput)
        //      {
        //        throw new UTXOException(string.Format("Double spent output. TX = '{0}', index = '{1}'.",
        //          Bytes2HexStringReversed(input.TXIDOutput),
        //          input.IndexOutput));
        //      }
        //    }

        //    int[] temp = new int[outputIndexes.Length + 1];
        //    outputIndexes.CopyTo(temp, 0);
        //    temp[outputIndexes.Length] = input.IndexOutput;

        //    outputIndexes = temp;
        //  }
        //  else
        //  {
        //    UTXOBuilder.InputsUnfunded.Add(input.TXIDOutput, new int[1] { input.IndexOutput });
        //  }
        //}

        //static void InsertTXOutputs(
        //  UInt256 headerHash,
        //  TX tX,
        //  byte[] tXHash)
        //{
        //  byte[] uTXO = CreateUTXO(headerHash, tX.Outputs.Count);

        //  if (UTXOBuilder.InputsUnfunded.TryGetValue(tXHash, out int[] outputIndexes))
        //  {
        //    SpendOutputs(uTXO, outputIndexes);
        //    UTXOBuilder.InputsUnfunded.Remove(tXHash);
        //  }

        //  if (!AreAllOutputBitsSpent(uTXO))
        //  {
        //    UTXOsBatch.Add(tXHash, uTXO);
        //  }
        //}
      }
    }
  }
}
