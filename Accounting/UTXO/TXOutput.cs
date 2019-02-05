using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using BToken.Networking;

namespace BToken.Accounting
{
  class TXOutput
  {
    public UInt64 Value { get; private set; }
    public byte[] LockingScript { get; private set; }

    public TXOutput(
      UInt64 value,
      byte[] lockingScript)
    {
      Value = value;
      LockingScript = lockingScript;
    }

    public static TXOutput Parse(byte[] byteStream, ref int startIndex)
    {
      UInt64 value = BitConverter.ToUInt64(byteStream, startIndex);
      startIndex += 8;

      int lockingScriptLength = (int)VarInt.GetUInt64(byteStream, ref startIndex);
      byte[] lockingScript = new byte[lockingScriptLength];
      Array.Copy(byteStream, startIndex, lockingScript, 0, lockingScriptLength);
      startIndex += lockingScriptLength;

      return new TXOutput(
        value,
        lockingScript);
    }

    public byte[] GetBytes()
    {
      List<byte> byteStream = new List<byte>();

      byteStream.AddRange(BitConverter.GetBytes(Value));
      byteStream.AddRange(VarInt.GetBytes(LockingScript.Length));
      byteStream.AddRange(LockingScript);

      return byteStream.ToArray();
    }

  }
}
