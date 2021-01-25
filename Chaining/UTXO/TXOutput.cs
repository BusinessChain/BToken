using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;


namespace BToken.Chaining
{
  partial class UTXOTable
  {
    public struct TXOutput
    {
      public ulong Value;
      public int StartIndexScript;
      public int LengthScript;


      public TXOutput(
        byte[] buffer,
        ref int index)
      {
        Value = BitConverter.ToUInt64(
          buffer,
          index);

        index += 8;

        LengthScript = VarInt.GetInt32(
          buffer,
          ref index);

        StartIndexScript = index;
        index += LengthScript;
      }

    }
  }
}
