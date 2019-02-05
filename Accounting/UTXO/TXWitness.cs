using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using BToken.Networking;

namespace BToken.Accounting
{
  class TXWitness
  {
    public static TXWitness Parse(byte[] byteStream, ref int startIndex)
    {
      return new TXWitness();
    }

    public byte[] GetBytes()
    {
      return new byte[0];
    }
  }
}
