using System;
using System.Collections.Generic;

using BToken.Networking;

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
      public IBlockPayload BlockPayload;

      public ChainBlock(NetworkHeader header)
      {
        Header = header;
      }

      public ChainBlock(
      UInt32 version,
      UInt256 hashPrevious,
      UInt256 merkleRootHash,
      UInt32 unixTimeSeconds,
      UInt32 nBits,
      UInt32 nonce)
      {
        Header = new NetworkHeader(
          version, 
          hashPrevious,
          merkleRootHash, 
          unixTimeSeconds, 
          nBits, 
          nonce);
      }

    }
  }
}
