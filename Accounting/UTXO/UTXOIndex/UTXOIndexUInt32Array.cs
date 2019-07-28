using System;
using System.Collections.Generic;
using System.Linq;

namespace BToken.Accounting
{
  public partial class UTXO
  {
    class UTXOIndexUInt32Array
    {
      public Dictionary<byte[], uint[]> Table =
        new Dictionary<byte[], uint[]>(new EqualityComparerByteArray());

      uint[] UTXOItem;
      
      uint[] MasksCollisionBitsClear = {
        0xFFCFFFFF,
        0xFF3FFFFF,
        0xFCFFFFFF };
      uint[] MasksCollisionBitsOne = {
        0x00100000,
        0x00400000,
        0x01000000 };
      uint[] MasksCollisionBitsTwo = {
        0x00200000,
        0x00800000,
        0x02000000 };
      uint[] MasksCollisionBitsFull = {
        0x00300000,
        0x00C00000,
        0x03000000 };

      static readonly uint MaskBatchIndex = ~(uint.MaxValue << COUNT_BATCHINDEX_BITS);
      static readonly uint MaskHeaderBits =
        ~((uint.MaxValue << (COUNT_BATCHINDEX_BITS + COUNT_HEADER_BITS)) | MaskBatchIndex);

      static readonly uint MaskAllOutputsBitsInFirstUInt32 = uint.MaxValue << CountNonOutputBits;


      public UTXOIndexUInt32Array()
      { }
      

      public void ParseUTXO(
        int batchIndex,
        byte[] headerHash,
        int lengthUTXOBits,
        byte[] tXHash)
      {
        uint[] uTXOIndex = new uint[(lengthUTXOBits + 31) / 32];

        int countUTXORemainderBits = lengthUTXOBits % 32;
        if (countUTXORemainderBits > 0)
        {
          uTXOIndex[uTXOIndex.Length - 1] |= (uint.MaxValue << countUTXORemainderBits);
        }

        uTXOIndex[0] = (uint)batchIndex & MaskBatchIndex;
        uTXOIndex[0] |= ((uint)headerHash[0] << COUNT_BATCHINDEX_BITS) & MaskHeaderBits;
        
        Table.Add(tXHash, uTXOIndex);
      }

      public bool TrySpend(TXInput input)
      {
        if (Table.TryGetValue(input.TXIDOutput, out UTXOItem))
        {
          SpendUTXO(UTXOItem, input.OutputIndex, out bool allOutputsSpent);
          Table[input.TXIDOutput] = UTXOItem;

          if (allOutputsSpent)
          {
            Table.Remove(input.TXIDOutput);
          }

          return true;
        }

        return false;
      }
      static void SpendUTXO(
        uint[] uTXO, 
        int outputIndex, 
        out bool areAllOutputpsSpent)
      {
        int bitOffset = CountNonOutputBits + outputIndex;
        int uintIndex = bitOffset / 32;
        int bitIndex = bitOffset % 32;

        uint mask = (uint)1 << bitIndex;
        if ((uTXO[uintIndex] & mask) != 0x00)
        {
          throw new UTXOException(string.Format(
            "Output index {0} already spent.", outputIndex));
        }
        uTXO[uintIndex] |= mask;

        areAllOutputpsSpent = AreAllOutputBitsSpent(uTXO);
      }
      static bool AreAllOutputBitsSpent(uint[] uTXO)
      {
        if ((uTXO[0] & MaskAllOutputsBitsInFirstUInt32) != MaskAllOutputsBitsInFirstUInt32)
        {
          return false;
        }
        for(int intIndex = 1; intIndex < uTXO.Length; intIndex += 1)
        {
          if (uTXO[intIndex] != uint.MaxValue)
          {
            return false;
          }
        }

        return true;
      }

    }
  }
}
