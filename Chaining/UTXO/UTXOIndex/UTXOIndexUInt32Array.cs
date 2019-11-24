using System;
using System.Collections.Generic;
using System.Linq;

namespace BToken.Chaining
{
  partial class UTXOTable
  {
    public class UTXOIndexUInt32Array
    {
      public Dictionary<byte[], uint[]> Table =
        new Dictionary<byte[], uint[]>(new EqualityComparerByteArray());

      uint[] UTXOItem;

      public static readonly uint MaskBatchIndex = ~(uint.MaxValue << COUNT_BATCHINDEX_BITS);
      static readonly uint MaskAllOutputsBitsInFirstUInt32 = uint.MaxValue << COUNT_NON_OUTPUT_BITS;


      public UTXOIndexUInt32Array()
      { }


      public void ParseUTXO(
        int lengthUTXOBits,
        byte[] tXHash)
      {
        uint[] uTXOIndex = new uint[(lengthUTXOBits + 31) / 32];

        int countUTXORemainderBits = lengthUTXOBits % 32;
        if (countUTXORemainderBits > 0)
        {
          uTXOIndex[uTXOIndex.Length - 1] |= (uint.MaxValue << countUTXORemainderBits);
        }
       
        Table.Add(tXHash, uTXOIndex);
      }

      public bool TrySpend(in TXInput input)
      {
        if (Table.TryGetValue(input.TXIDOutput, out UTXOItem))
        {
          int bitOffset = COUNT_NON_OUTPUT_BITS + input.OutputIndex;
          int uintIndex = bitOffset / 32;
          int bitIndex = bitOffset % 32;

          uint mask = (uint)1 << bitIndex;
          if ((UTXOItem[uintIndex] & mask) != 0x00)
          {
            throw new UTXOException(string.Format(
              "Output index {0} already spent.", input.OutputIndex));
          }
          UTXOItem[uintIndex] |= mask;

          Table[input.TXIDOutput] = UTXOItem;


          if ((UTXOItem[0] & MaskAllOutputsBitsInFirstUInt32) != MaskAllOutputsBitsInFirstUInt32)
          {
            return true;
          }
          for (int intIndex = 1; intIndex < UTXOItem.Length; intIndex += 1)
          {
            if (UTXOItem[intIndex] != uint.MaxValue)
            {
              return true;
            }
          }

          Table.Remove(input.TXIDOutput);

          return true;
        }

        return false;
      }
      static bool AreAllOutputBitsSpent(uint[] uTXO)
      {
        if ((uTXO[0] & MaskAllOutputsBitsInFirstUInt32) != MaskAllOutputsBitsInFirstUInt32)
        {
          return false;
        }
        for (int intIndex = 1; intIndex < uTXO.Length; intIndex += 1)
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
