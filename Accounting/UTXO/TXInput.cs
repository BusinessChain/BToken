using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using BToken.Networking;

namespace BToken.Accounting.UTXO
{
  class TXInput
  {
    public UInt256 TXIDOutput { get; private set; }
    public int IndexOutput { get; private set; }
    public byte[] UnlockingScript { get; private set; }
    UInt32 Sequence;


    public TXInput(
      UInt256 tXIDOutput,
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
      UInt256 tXIDOutput = new UInt256(byteStream, ref startIndex);

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

      byteStream.AddRange(TXIDOutput.GetBytes());
      byteStream.AddRange(BitConverter.GetBytes(IndexOutput));
      byteStream.AddRange(VarInt.GetBytes(UnlockingScript.Length));
      byteStream.AddRange(UnlockingScript);
      byteStream.AddRange(BitConverter.GetBytes(Sequence));

      return byteStream.ToArray();
    }

  }
}
