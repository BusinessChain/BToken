using System.Collections.Generic;
using System.Collections;
using System;

namespace BToken.Accounting.UTXO
{
  partial class UTXO
  {
    class TXOutputsSpentMap
    {
      public List<TXOutput> TXOutputs;
      public byte[] FlagsOutputsSpent;

      public TXOutputsSpentMap(List<TXOutput> tXOutputs)
      {
        TXOutputs = tXOutputs;
        FlagsOutputsSpent = new byte[(tXOutputs.Count + 7) / 8];
      }

      public byte[] GetBytes()
      {
        return null;
      }
    }
  }
}