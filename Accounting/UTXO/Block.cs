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

      public Block(
        byte[] buffer, 
        int bufferIndex, 
        byte[] headerHash, 
        int tXcount)
      {
        Buffer = buffer;
        BufferIndex = bufferIndex;
        HeaderHash = headerHash;
        TXCount = tXcount;
      }
    }
  }
}
