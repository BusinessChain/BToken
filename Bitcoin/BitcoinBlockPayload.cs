using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using BToken.Chaining;

namespace BToken.Bitcoin
{
  public class BitcoinBlockPayload : Blockchain.IBlockPayload
  {
    List<BitcoinTX> BitcoinTXs = new List<BitcoinTX>();
    UInt256 MerkleRootHash; // Get rid of this, it is only needed for fake GenesisBlock



    public BitcoinBlockPayload()
    {
    }
    public BitcoinBlockPayload(UInt256 merkleRootHash)
    {
      MerkleRootHash = merkleRootHash;
    }
    public BitcoinBlockPayload(List<BitcoinTX> bitcoinTXs)
    {
      BitcoinTXs = bitcoinTXs;
    }

    public UInt256 GetPayloadHash()
    {
      return MerkleRootHash ?? ComputeMerkleRootHash();
    }
    UInt256 ComputeMerkleRootHash()
    {
      const int HASH_BYTE_SIZE = 32;


      List<byte[]> merkleList = BitcoinTXs.Select(b =>
      {
        byte[] txBytes = b.GetBytes();
        return Hashing.sha256d(txBytes);
      }).ToList();
      
      return new UInt256(GetRoot(merkleList, HASH_BYTE_SIZE));
    }
    byte[] GetRoot(List<byte[]> merkleList, int hashByteSize)
    {
      while (merkleList.Count > 1)
      {
        if (merkleList.Count % 2 != 0)
        {
          merkleList.Add(merkleList.Last());
        }

        var merkleListNext = new List<byte[]>();
        for (int i = 0; i < merkleList.Count; i += 2)
        {
          byte[] leafPairHashesConcat = ConcatenateArrays(merkleList[i], merkleList[i + 1], hashByteSize);
          merkleListNext.Add(Hashing.sha256d(leafPairHashesConcat));
        }

        merkleList = merkleListNext;
      }

      return merkleList[0];
    }
    byte[] ConcatenateArrays(byte[] array1, byte[] array2, int arraySize)
    {
      var arrayConcat = new byte[2 * arraySize];
      array1.CopyTo(arrayConcat, 0);
      array2.CopyTo(arrayConcat, arraySize);

      return arrayConcat;
    }

    public void ParsePayload(byte[] stream)
    {
      throw new NotImplementedException();
    }
  }
}
