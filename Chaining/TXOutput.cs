using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using BToken.Networking;

namespace BToken.Chaining
{
  class TXOutput
  {
    public UInt64 Value { get; private set; }
    public byte[] LockingScript { get; private set; }

    public TXOutput(UInt64 value)
    {
      Value = value;
    }

    public static TXOutput Parse(byte[] byteStream, ref int startIndex)
    {
      UInt64 value = BitConverter.ToUInt64(byteStream, startIndex);
      startIndex += 8;

      int lockingScriptLength = VarInt.GetInt32(byteStream, ref startIndex);
      //byte[] lockingScript = new byte[lockingScriptLength];
      //Array.Copy(byteStream, startIndex, lockingScript, 0, lockingScriptLength);
      startIndex += lockingScriptLength;

      return new TXOutput(value);
    }

  }
}
