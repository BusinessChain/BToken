


namespace BToken.Chaining
{
  public partial class Blockchain
  {
    class Block
    {
      public byte[] Buffer;
      public int BufferIndex;
      public Header Header;
      public byte[] HeaderHash;
      public int TXCount;

      public Block(
        byte[] buffer, 
        int bufferIndex,
        Header header,
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
