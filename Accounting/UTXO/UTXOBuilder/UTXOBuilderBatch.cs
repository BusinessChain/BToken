using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.Remoting.Metadata.W3cXsd2001;
using System.Threading.Tasks;

using BToken.Chaining;

namespace BToken.Accounting
{
  public partial class UTXO
  {
    partial class UTXOBuilder
    {
      class UTXOBuilderBatch
      {
        UTXO UTXO;
        UTXOBuilder UTXOBuilder;

        List<HeaderLocation> HeaderLocations;
        public Dictionary<byte[], List<TXInput>> InputsUnfunded;
        public Dictionary<byte[], byte[]> UTXOs;

        public UTXOBuilderBatch(
          UTXO uTXO, 
          UTXOBuilder uTXOBuilder,
          List<HeaderLocation> headerLocations)
        {
          UTXO = uTXO;
          UTXOBuilder = uTXOBuilder;
          HeaderLocations = headerLocations;
          InputsUnfunded = new Dictionary<byte[], List<TXInput>>(new EqualityComparerByteArray());
          UTXOs = new Dictionary<byte[], byte[]>(new EqualityComparerByteArray());
        }

        public async Task BuildAsync()
        {

          foreach (HeaderLocation headerLocation in HeaderLocations)
          {
            Block block = await UTXO.GetBlockAsync(headerLocation.Hash);
            BuildBlock(block);

            Console.WriteLine("'{0};{1};{2};{3}",
              headerLocation.Hash,
              headerLocation.Height,
              InputsUnfunded.Count,
              UTXOs.Count);
          }

          await UTXOBuilder.MergeBatchAsync(this);
        }

        void BuildBlock(Block block)
        {
          List<TX> tXs = block.TXs;
          List<byte[]> tXHashes = block.TXHashes;
          UInt256 headerHash = block.HeaderHash;

          for (int t = 1; t < tXs.Count; t++)
          {
            for (int i = 0; i < tXs[t].Inputs.Count; i++)
            {
              AddInputUnfunded(tXs[t].Inputs[i]);
            }
          }

          for (int t = 0; t < tXs.Count; t++)
          {
            AddUTXO(headerHash, tXs[t], tXHashes[t]);
          }
        }
        void AddInputUnfunded(TXInput input)
        {
          if (InputsUnfunded.TryGetValue(input.TXIDOutput, out List<TXInput> inputs))
          {
            if (inputs.Any(tu => tu.IndexOutput == input.IndexOutput))
            {
              throw new UTXOException("Double spend detected during UTXO build.");
            }
            else
            {
              inputs.Add(input);
            }
          }
          else
          {
            InputsUnfunded.Add(input.TXIDOutput, new List<TXInput> { input });
          }
        }
        void AddUTXO(UInt256 headerHash, TX tX, byte[] tXHash)
        {
          byte[] uTXO = CreateUTXO(headerHash, tX.Outputs.Count);

          if (InputsUnfunded.TryGetValue(tXHash, out List<TXInput> inputs))
          {
            SpendOutputBits(uTXO, inputs);
            InputsUnfunded.Remove(tXHash);
          }

          if (!AreAllOutputBitsSpent(uTXO))
          {
            try
            {
              UTXOs.Add(tXHash, uTXO);
            }
            catch (ArgumentException)
            {
              Console.WriteLine("Ambiguous transaction '{0}' in block '{1}'",
                new SoapHexBinary(tXHash), headerHash);
            }
          }
        }
        byte[] CreateUTXO(UInt256 headerHash, int outputsCount)
        {
          byte[] uTXOIndex = new byte[CountHeaderIndexBytes + (outputsCount + 7) / 8];
          for (int i = outputsCount % 8; i < 8; i++)
          {
            uTXOIndex[uTXOIndex.Length - 1] |= (byte)(0x01 << i);
          }

          Array.Copy(headerHash.GetBytes(), uTXOIndex, CountHeaderIndexBytes);
          return uTXOIndex;
        }

      }
    }
  }
}
