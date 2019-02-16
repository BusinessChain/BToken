using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using BToken.Networking;

namespace BToken.Accounting
{
  class TXInput
  {
    public byte[] TXIDOutput { get; private set; }
    public int IndexOutput { get; private set; }
    public byte[] UnlockingScript { get; private set; }
    UInt32 Sequence;


    public TXInput(
      byte[] tXIDOutput,
      int indexOutput,
      byte[] unlockingScript,
      UInt32 sequence)
    {
      TXIDOutput = tXIDOutput;
      IndexOutput = indexOutput;
      UnlockingScript = unlockingScript;
      Sequence = sequence;
    }

    public static TXInput Parse(byte[] byteStream, ref int startIndex)
    {
      byte[] tXIDOutput = new UInt256(byteStream, ref startIndex).GetBytes();

      UInt32 indexOutput = BitConverter.ToUInt32(byteStream, startIndex);
      startIndex += 4;

      int unlockingScriptLength = (int)VarInt.GetUInt64(byteStream, ref startIndex);
      byte[] unlockingScript = new byte[unlockingScriptLength];
      Array.Copy(byteStream, startIndex, unlockingScript, 0, unlockingScriptLength);
      startIndex += unlockingScriptLength;

      UInt32 sequence = BitConverter.ToUInt32(byteStream, startIndex);
      startIndex += 4;

      return new TXInput(
        tXIDOutput,
        (int)indexOutput,
        unlockingScript,
        sequence);
    }

    public byte[] GetBytes()
    {
      List<byte> byteStream = new List<byte>();

      byteStream.AddRange(TXIDOutput);
      byteStream.AddRange(BitConverter.GetBytes(IndexOutput));
      byteStream.AddRange(VarInt.GetBytes(UnlockingScript.Length));
      byteStream.AddRange(UnlockingScript);
      byteStream.AddRange(BitConverter.GetBytes(Sequence));

      return byteStream.ToArray();
    }

  }
}
