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

      ulong MaskAllOutputBitsSpent = ulong.MaxValue << CountNonOutputBits;
      ulong MaskBatchIndex = ~(ulong.MaxValue << COUNT_BATCHINDEX_BITS);
      

      public UTXOIndexULong64()
      { }


      public void ParseUTXO(
        int batchIndex,
        int lengthUTXOBits,
        byte[] tXHash)
      {
        ulong uTXOIndex = (uint)batchIndex & MaskBatchIndex;

        if (COUNT_LONG_BITS > lengthUTXOBits)
        {
          uTXOIndex |= (ulong.MaxValue << lengthUTXOBits);
        }

        Table.Add(tXHash, uTXOIndex);
      }

      public bool TrySpend(in TXInput input)
      {
        if (Table.TryGetValue(input.TXIDOutput, out UTXOItem))
        {
          ulong mask = (ulong)1 << (CountNonOutputBits + input.OutputIndex);
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
