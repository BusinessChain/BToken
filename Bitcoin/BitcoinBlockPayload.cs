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
    class BitcoinBlockPayload : Blockchain.IBlockPayload
    {
      List<BitcoinTX> BitcoinTXs;
      UInt256 MerkleRootHash;



      public BitcoinBlockPayload(UInt256 merkleRootHash)
      {
        MerkleRootHash = merkleRootHash;
      }
      public BitcoinBlockPayload(List<BitcoinTX> bitcoinTXs)
      {
        BitcoinTXs = bitcoinTXs;
        MerkleRootHash = ComputeHash(bitcoinTXs);
      }

      public UInt256 ComputeHash(List<BitcoinTX> bitcoinTXs)
      {
        return MerkleRootHash;
      }
      public UInt256 ComputeHash()
      {
        return MerkleRootHash;
      }

      public void ParsePayload(byte[] stream)
      {
        throw new NotImplementedException();
      }
    }
  }
}
