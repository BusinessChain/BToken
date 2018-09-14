using System;
using System.Collections.Generic;

using BToken.Networking;

namespace BToken.Chaining
{
  public interface IBlockPayload
  {
    void ParsePayload(byte[] stream);
    UInt256 GetPayloadHash();
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
        payload.GetPayloadHash(),
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
      if (!payload.GetPayloadHash().IsEqual(Header.PayloadHash))
      {
        throw new BlockchainException(BlockCode.INVALID);
      }

      BlockPayload = payload;
    }

    public bool IsPayloadAssigned() => BlockPayload != null;
  }
}
