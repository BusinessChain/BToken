using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;


namespace BToken.Accounting
{
  public partial class UTXO
  {
    class TXInput
    {
      public int IndexTXIDOutput;
      public int OutputIndex;


      public TXInput(byte[] byteStream, ref int startIndex)
      {
        IndexTXIDOutput = startIndex;
        startIndex += 32;

        OutputIndex = BitConverter.ToInt32(byteStream, startIndex);
        startIndex += 4;

        startIndex += VarInt.GetInt32(byteStream, ref startIndex); // length unlocking script
        startIndex += 4; // sequence
      }
    }
  }
}
