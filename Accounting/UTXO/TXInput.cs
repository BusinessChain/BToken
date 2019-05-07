using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;


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
      int indexOutput)
    {
      TXIDOutput = tXIDOutput;
      IndexOutput = indexOutput;;
    }

    public static TXInput Parse(byte[] byteStream, ref int startIndex)
    {
      byte[] tXIDOutput = new byte[32];
      Array.Copy(byteStream, startIndex, tXIDOutput, 0, 32);
      startIndex += 32;

      int indexOutput = BitConverter.ToInt32(byteStream, startIndex);
      startIndex += 4;

      int unlockingScriptLength = VarInt.GetInt32(byteStream, ref startIndex);
      //byte[] unlockingScript = new byte[unlockingScriptLength];
      //Array.Copy(byteStream, startIndex, unlockingScript, 0, unlockingScriptLength);
      startIndex += unlockingScriptLength;

      //UInt32 sequence = BitConverter.ToUInt32(byteStream, startIndex);
      startIndex += 4;

      return new TXInput(
        tXIDOutput,
        indexOutput);
    }
  }
}
