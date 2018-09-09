using System;
using System.Collections.Generic;

using BToken.Networking;

namespace BToken.Chaining
{
  partial class Blockchain
  {
    public interface IBlockPayload
    {
      void ParsePayload(byte[] stream);
      UInt256 ComputeHash();
    }

    public class ChainBlock
    {
      public NetworkHeader Header;

      public ChainBlock BlockPrevious;
      public List<ChainBlock> BlocksNext = new List<ChainBlock>();
      IBlockPayload BlockPayload;

      public ChainBlock(
        UInt32 version,
        UInt256 hashPrevious,
        UInt32 unixTimeSeconds,
        UInt32 nBits,
        UInt32 nonce,
        IBlockPayload payload)
      {
        Header = new NetworkHeader(
          version,
          hashPrevious,
          payload.ComputeHash(),
          unixTimeSeconds,
          nBits,
          nonce);

        InsertPayload(payload);
      }
      
      public ChainBlock(NetworkHeader header)
      {
        Header = header;
      }

      public void InsertPayload(IBlockPayload payload)
      {
        BlockPayload = payload;

        if (!BlockPayload.ComputeHash().isEqual(Header.PayloadHash))
        {
          throw new BlockchainException(BlockCode.INVALID);
        }
      }

      public bool IsPayloadAssigned()
      {
        return BlockPayload != null;
      }
    }
  }
}
