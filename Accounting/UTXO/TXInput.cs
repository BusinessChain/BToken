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
    public int[] TXIDOutput { get; private set; }
    public int IndexOutput { get; private set; }
    public byte[] UnlockingScript { get; private set; }
    UInt32 Sequence;


    public TXInput(
      int[] tXIDOutput,
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
      int[] tXIDOutput = new UInt256(byteStream, ref startIndex).GetIntegers();

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
      List<byte> bytes = new List<byte>();

      byte[] tXIDOutputBytes = new byte[TXIDOutput.Length * sizeof(int)];
      Buffer.BlockCopy(TXIDOutput, 0, tXIDOutputBytes, 0, tXIDOutputBytes.Length);

      bytes.AddRange(tXIDOutputBytes);
      bytes.AddRange(BitConverter.GetBytes(IndexOutput));
      bytes.AddRange(VarInt.GetBytes(UnlockingScript.Length));
      bytes.AddRange(UnlockingScript);
      bytes.AddRange(BitConverter.GetBytes(Sequence));

      return bytes.ToArray();
    }

  }
}
