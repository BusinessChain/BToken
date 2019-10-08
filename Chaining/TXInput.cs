using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;


namespace BToken.Chaining
{
  partial class UTXOTable : IDatabase
  {
    public struct TXInput
    {
      public byte[] TXIDOutput;
      public int PrimaryKeyTXIDOutput;
      public int OutputIndex;


      public TXInput(byte[] buffer, ref int startIndex)
      {
        PrimaryKeyTXIDOutput = BitConverter.ToInt32(buffer, startIndex);
        TXIDOutput = new byte[HASH_BYTE_SIZE];
        Array.Copy(buffer, startIndex, TXIDOutput, 0, HASH_BYTE_SIZE);
        startIndex += HASH_BYTE_SIZE;

        OutputIndex = BitConverter.ToInt32(buffer, startIndex);
        startIndex += 4;

        int lengthUnlockingScript = VarInt.GetInt32(buffer, ref startIndex);
        startIndex += lengthUnlockingScript;

        startIndex += 4; // sequence
      }
    }
  }
}
