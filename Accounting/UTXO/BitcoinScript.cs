using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace BToken.Accounting.UTXO
{
  class BitcoinScript
  {
    public static bool Evaluate(byte[] lockingScript, byte[] unlockingScript)
    {
      return true;
    }
  }
}
