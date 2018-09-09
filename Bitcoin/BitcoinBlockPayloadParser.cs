using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using BToken.Chaining;

namespace BToken
{
  partial class Bitcoin
  {
    class BitcoinBlockPayloadParser : Blockchain.IBlockPayloadParser
    {
      public Blockchain.IBlockPayload Parse(byte[] stream)
      {
        //var networkTXs = new List<NetworkTX>();
        //for (int i = 0; i < txCount; i++)
        //{
        //  networkTXs.Add(NetworkTX.Parse(blockBytes, ref startIndex));
        //}

        return new BitcoinGenesisBlock() as Blockchain.IBlockPayload;
      }
    }
  }
}
