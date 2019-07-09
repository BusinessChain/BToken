
using BToken.Chaining;


namespace BToken.Accounting
{
  public partial class UTXO
  {
    class Block
    {
      public byte[] Buffer;
      public int BufferIndex;
      public Headerchain.ChainHeader Header;
      public byte[] HeaderHash;
      public int TXCount;

      public Block(
        byte[] buffer, 
        int bufferIndex,
        Headerchain.ChainHeader header,
        byte[] headerHash, 
        int tXcount)
      {
        Buffer = buffer;
        BufferIndex = bufferIndex;
        Header = header;
        HeaderHash = headerHash;
        TXCount = tXcount;
      }
    }
  }
}
