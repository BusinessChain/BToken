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
      UInt256 MerkleRootHash;



      public BitcoinBlockPayload(UInt256 merkleRootHash)
      {
        MerkleRootHash = merkleRootHash;
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
