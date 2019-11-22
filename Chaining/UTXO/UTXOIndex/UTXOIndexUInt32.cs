using System;
using System.Collections.Generic;
using System.Linq;

namespace BToken.Chaining
{
  partial class UTXOTable
  {
    public class UTXOIndexUInt32
    {
      public Dictionary<byte[], uint> Table =
        new Dictionary<byte[], uint>(new EqualityComparerByteArray());

      uint UTXOItem;

      readonly uint MaskAllOutputBitsSpent = uint.MaxValue << COUNT_NON_OUTPUT_BITS;
      public static readonly uint MaskBatchIndex = ~(uint.MaxValue << COUNT_BATCHINDEX_BITS);


      public UTXOIndexUInt32()
      { }

      public void ParseUTXO(
        int lengthUTXOBits,
        byte[] tXHash)
      {
        uint uTXOIndex = 0;

        if (LENGTH_BITS_UINT > lengthUTXOBits)
        {
          uTXOIndex |= (uint.MaxValue << lengthUTXOBits);
        }

        try
        {
          Table.Add(tXHash, uTXOIndex);
        }
        catch (ArgumentException)
        {
          // BIP 30
          if (tXHash.ToHexString() == "D5D27987D2A3DFC724E359870C6644B40E497BDC0589A033220FE15429D88599" ||
             tXHash.ToHexString() == "E3BF3D07D4B0375638D5F1DB5255FE07BA2C4CB067CD81B84EE974B6585FB468")
          {
            Table[tXHash] = uTXOIndex;
          }
        }
      }

      public bool TrySpend(in TXInput input)
      {
        if (Table.TryGetValue(input.TXIDOutput, out UTXOItem))
        {
          uint mask = (uint)1 << (COUNT_NON_OUTPUT_BITS + input.OutputIndex);
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
