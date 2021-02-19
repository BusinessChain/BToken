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

      public byte[] Buffer;
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

        Buffer = buffer;

        LengthScript = VarInt.GetInt32(
          Buffer,
          ref index);

        StartIndexScript = index;
        index += LengthScript;
      }
    }
  }
}
