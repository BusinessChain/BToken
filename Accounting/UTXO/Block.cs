using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace BToken.Accounting
{
  public partial class UTXO
  {
    class Block
    {
      public byte[] Buffer;
      public int BufferIndex;
      public byte[] HeaderHash;
      public int TXCount;

      public Block(byte[] buffer, int bufferIndex)
      {
        Buffer = buffer;
        BufferIndex = bufferIndex;
      }

      public Block(
        byte[] buffer,
        byte[] headerHash,
        int tXCount)
      {
        Buffer = buffer;
        HeaderHash = headerHash;
        TXCount = tXCount;
      }
    }
  }
}
