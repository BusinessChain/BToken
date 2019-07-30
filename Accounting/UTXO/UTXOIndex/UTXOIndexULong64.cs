using System;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using System.Threading.Tasks;

namespace BToken.Accounting
{
  public partial class UTXO
  {
    class UTXOIndexULong64
    {
      public Dictionary<byte[], ulong> Table =
        new Dictionary<byte[], ulong>(COUNT_TXS_IN_BATCH_FILE, new EqualityComparerByteArray());
      
      ulong UTXOItem;

      static readonly ulong MaskAllOutputBitsSpent = ulong.MaxValue << CountNonOutputBits;
      static readonly ulong MaskBatchIndex = ~(ulong.MaxValue << COUNT_BATCHINDEX_BITS);
      static readonly ulong MaskHeaderBits =
        ~((uint.MaxValue << (COUNT_BATCHINDEX_BITS + COUNT_HEADER_BITS)) | MaskBatchIndex);

      ulong[] MasksCollisionBitsClear = {
        0xFFFFFFFFFFCFFFFF,
        0xFFFFFFFFFF3FFFFF,
        0xFFFFFFFFFCFFFFFF };
      ulong[] MasksCollisionBitsOne = {
        0x00100000,
        0x00400000,
        0x01000000 };
      ulong[] MasksCollisionBitsTwo = {
        0x00200000,
        0x00800000,
        0x02000000 };
      ulong[] MasksCollisionBitsFull = {
        0x00300000,
        0x00C00000,
        0x03000000 };


      public UTXOIndexULong64()
      { }


      public void ParseUTXO(
        int batchIndex,
        byte[] headerHash,
        int lengthUTXOBits,
        byte[] tXHash)
      {
        ulong uTXOIndex = (uint)batchIndex & MaskBatchIndex;
        uTXOIndex |= ((uint)headerHash[0] << COUNT_BATCHINDEX_BITS) & MaskHeaderBits;

        if (COUNT_LONG_BITS > lengthUTXOBits)
        {
          uTXOIndex |= (ulong.MaxValue << lengthUTXOBits);
        }

        Table.Add(tXHash, uTXOIndex);
      }

      public bool TrySpend(TXInput input)
      {
        if (Table.TryGetValue(input.TXIDOutput, out UTXOItem))
        {
          SpendUTXO(ref UTXOItem, input.OutputIndex, out bool allOutputsSpent);
          Table[input.TXIDOutput] = UTXOItem;

          if (allOutputsSpent)
          {
            Table.Remove(input.TXIDOutput);
          }

          return true;
        }

        return false;
      }
      static void SpendUTXO(ref ulong uTXO, int outputIndex, out bool areAllOutputpsSpent)
      {
        ulong mask = (ulong)1 << (CountNonOutputBits + outputIndex);
        if ((uTXO & mask) != 0x00)
        {
          throw new UTXOException(string.Format(
            "Output index {0} already spent.", outputIndex));
        }
        uTXO |= mask;

        areAllOutputpsSpent = (uTXO & MaskAllOutputBitsSpent) == MaskAllOutputBitsSpent;
      }
      
    }
  }
}
