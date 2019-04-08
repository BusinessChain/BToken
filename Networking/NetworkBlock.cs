using System;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using System.Threading.Tasks;

namespace BToken.Networking
{
  public class NetworkBlock
  {
    public NetworkHeader Header { get; private set; }
    public int TXCount { get; private set; }
    public byte[] Payload { get; private set; }
    public byte[] BlockBytes { get; private set; }


    public NetworkBlock(
      NetworkHeader networkHeader,
      int txCount,
      byte[] blockBytes)
    {
      Header = networkHeader;
      TXCount = txCount;
      BlockBytes = blockBytes;
    }

    public NetworkBlock(
      NetworkHeader networkHeader, 
      int txCount, 
      byte[] payload, 
      byte[] blockBytes)
    {
      Header = networkHeader;
      TXCount = txCount;
      Payload = payload;
      BlockBytes = blockBytes;
    }


    public static bool TryReadBlock(
      byte[] blockBytes, 
      ref int index, 
      out NetworkBlock networkBlock)
    {
      NetworkHeader header = NetworkHeader.ParseHeader(
        blockBytes, 
        out int txCount, 
        ref index);

      return new NetworkBlock(header, txCount, blockBytes);
    }

    public static NetworkBlock ReadBlock(byte[] blockBytes)
    {
      int index = 0;

      NetworkHeader header = NetworkHeader.ParseHeader(blockBytes, out int txCount, ref index);
      byte[] payload = blockBytes.Skip(index).ToArray(); // increase performance by skipping that, instead keep an index in the object pointing to the start  of the block payload.

      return new NetworkBlock(header, txCount, payload, blockBytes);
    }
  }
}
