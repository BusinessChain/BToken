using System;
using System.Collections.Generic;
using System.Linq;


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
      public UInt256 Hash;
      public UInt256 HashPrevious;
      public UInt64 UnixTimeSeconds;
      public UInt256 MerkleRootHash;
      public UInt32 NBits;

      public ChainBlock BlockPrevious;
      public List<ChainBlock> BlocksNext = new List<ChainBlock>();
      public IBlockPayload BlockPayload;

      public ChainBlock(
        UInt256 hash,
        UInt256 hashPrevious,
        UInt32 nBits,
        UInt256 merkleRootHash,
        UInt64 unixTimeSeconds)
      {
        Hash = hash;
        HashPrevious = hashPrevious;
        UnixTimeSeconds = unixTimeSeconds;
        MerkleRootHash = merkleRootHash;
        NBits = nBits;
      }

    }
  }
}
