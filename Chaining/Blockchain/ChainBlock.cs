using System.Collections.Generic;


namespace BToken.Chaining
{
  partial class Blockchain
  {
    public interface IBlockPayload
    {
      UInt256 ComputeMerkleRootHash();
    }

    public class ChainBlock
    {
      public NetworkHeader Header;

      public ChainBlock BlockPrevious;
      public List<ChainBlock> BlocksNext = new List<ChainBlock>();
      //public IBlockPayload BlockPayload;

      public ChainBlock(NetworkHeader header)
      {
        Header = header;
      }

    }
  }
}
