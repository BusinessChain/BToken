using System.Collections.Generic;

namespace BToken.Accounting.Bitcoin
{
  internal class TXOutputsSpentMap : TXOutputsUnspentMap
  {
    public TXOutputsSpentMap(List<TXOutput> tXOutputs) : base(tXOutputs)
    {
    }
  }
}