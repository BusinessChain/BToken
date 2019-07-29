using System;
using System.Collections.Generic;
using System.Linq;

namespace BToken.Accounting
{
  public partial class UTXO
  {
    class UTXOIndexUInt32
    {
      public KeyValuePair<byte[], uint>[] UTXOItemsUInt32 
        = new KeyValuePair<byte[], uint>[COUNT_TXS_IN_BATCH_FILE];
      public int IndexUTXOs;

      public Dictionary<byte[], uint> Table =
        new Dictionary<byte[], uint>(new EqualityComparerByteArray());
      
      uint UTXOItem;

      static readonly uint MaskAllOutputBitsSpent = uint.MaxValue << CountNonOutputBits;
      static readonly uint MaskBatchIndex = ~(uint.MaxValue << COUNT_BATCHINDEX_BITS);
      static readonly uint MaskHeaderBits =
        ~((uint.MaxValue << (COUNT_BATCHINDEX_BITS + COUNT_HEADER_BITS)) | MaskBatchIndex);

      public readonly static uint[] MasksCollisionBitsClear = {
        0xFFCFFFFF,
        0xFF3FFFFF,
        0xFCFFFFFF };
      public readonly static uint[] MasksCollisionBitsOne = {
        0x00100000,
        0x00400000,
        0x01000000 };
      public readonly static uint[] MasksCollisionBitsTwo = {
        0x00200000,
        0x00800000,
        0x02000000 };
      public readonly static uint[] MasksCollisionBitsFull = {
        0x00300000,
        0x00C00000,
        0x03000000 };


      public UTXOIndexUInt32()
      { }      

      public void ParseUTXO(
        int batchIndex,
        byte[] headerHash,
        int lengthUTXOBits,
        byte[] tXHash)
      {
        uint uTXOIndex = (uint)batchIndex & MaskBatchIndex;
        uTXOIndex |= ((uint)headerHash[0] << COUNT_BATCHINDEX_BITS) & MaskHeaderBits;

        if (COUNT_INTEGER_BITS > lengthUTXOBits)
        {
          uTXOIndex |= (uint.MaxValue << lengthUTXOBits);
        }

        UTXOItemsUInt32[IndexUTXOs++] = new KeyValuePair<byte[], uint>(tXHash, uTXOIndex);
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
      static void SpendUTXO(ref uint uTXO, int outputIndex, out bool allOutputsSpent)
      {
        uint mask = (uint)1 << (CountNonOutputBits + outputIndex);
        if ((uTXO & mask) != 0x00)
        {
          throw new UTXOException(string.Format(
            "Output index {0} already spent.", outputIndex));
        }
        uTXO |= mask;

        allOutputsSpent = (uTXO & MaskAllOutputBitsSpent) == MaskAllOutputBitsSpent;
      }

    }
  }
}
