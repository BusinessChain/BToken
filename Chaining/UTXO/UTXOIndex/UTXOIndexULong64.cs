using System;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using System.Threading.Tasks;

namespace BToken.Chaining
{
  partial class UTXOTable
  {
    public class UTXOIndexULong64
    {
      public Dictionary<byte[], ulong> Table =
        new Dictionary<byte[], ulong>(new EqualityComparerByteArray());

      ulong UTXOItem;

      ulong MaskAllOutputBitsSpent = ulong.MaxValue << COUNT_NON_OUTPUT_BITS;
      public static ulong MaskBatchIndex = ~(ulong.MaxValue << COUNT_BATCHINDEX_BITS);


      public UTXOIndexULong64()
      { }


      public void ParseUTXO(
        int lengthUTXOBits,
        byte[] tXHash)
      {
        ulong uTXOIndex = 0;

        if (LENGTH_BITS_ULONG > lengthUTXOBits)
        {
          uTXOIndex |= (ulong.MaxValue << lengthUTXOBits);
        }

        Table.Add(tXHash, uTXOIndex);
      }

      public bool TrySpend(in TXInput input)
      {
        if (Table.TryGetValue(input.TXIDOutput, out UTXOItem))
        {
          ulong mask = (ulong)1 << (COUNT_NON_OUTPUT_BITS + input.OutputIndex);
          if ((UTXOItem & mask) != 0x00)
          {
            throw new UTXOException(string.Format(
              "Output index {0} already spent.", input.OutputIndex));
          }
          UTXOItem |= mask;

          Table[input.TXIDOutput] = UTXOItem;

          if ((UTXOItem & MaskAllOutputBitsSpent) == MaskAllOutputBitsSpent)
          {
            Table.Remove(input.TXIDOutput);
          }

          return true;
        }

        return false;
      }

    }
  }
}
