using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using BToken.Networking;

namespace BToken.Chaining
{
  partial class Blockchain : Chain
  {
    public class ChainBlock : ChainLink
    {
      public Headerchain.ChainHeader Header;
      List<TX> TXs = new List<TX>();

      public ChainBlock(NetworkBlock networkBlock)
      {
        // Der Header besteht im Memory ja bereits also kein neuen machen.
        Header = null;//new Headerchain.ChainHeader(networkBlock.Header);
        TXs = networkBlock.NetworkTXs.Select(ntx => new TX(ntx)).ToList();
      }
      public ChainBlock(
        UInt256 hash, 
        UInt256 hashPrevious,
        UInt32 nBits,
        UInt256 merkleRootHash,
        UInt64 unixTimeSeconds,
        List<TX> tXs)
      {
        Header = new Headerchain.ChainHeader(hash, hashPrevious, nBits, merkleRootHash, unixTimeSeconds);
        TXs = tXs;
      }
    }
  }
}
