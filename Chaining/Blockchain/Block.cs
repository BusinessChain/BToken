


namespace BToken.Chaining
{
  public partial class Blockchain
  {
    class Block
    {
      public byte[] Buffer;
      public int BufferIndex;
      public ChainHeader Header;
      public byte[] HeaderHash;
      public int TXCount;

      public Block(
        byte[] buffer, 
        int bufferIndex,
        ChainHeader header,
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
