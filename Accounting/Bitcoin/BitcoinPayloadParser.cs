using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using BToken.Chaining;

namespace BToken.Bitcoin
{
  public class BitcoinPayloadParser : Headerchain.IPayloadParser
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

    public UInt256 GetPayloadHash(byte[] payload)
    {
      IBlockPayload blockPayload = Parse(payload);
      return blockPayload.GetPayloadHash();
    }

    public void ValidatePayload()
    {
      throw new NotImplementedException();
    }
  }
}
