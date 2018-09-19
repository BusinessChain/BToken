using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using BToken.Chaining;

namespace BToken.Bitcoin
{
  public class BitcoinBlockPayloadParser : IBlockParser
  {
    public IBlockPayload Parse(byte[] payloadStream)
    {
      var bitcoinTXs = new List<BitcoinTX>();

      int startIndex = 0;
      while (startIndex < payloadStream.Length)
      {
        bitcoinTXs.Add(BitcoinTX.Parse(payloadStream, ref startIndex));
      }

      return new BitcoinBlockPayload(bitcoinTXs);
    }
  }
}
