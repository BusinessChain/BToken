using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using BToken.Chaining;

namespace BToken.Accounting.UTXO
{
  class PayloadParser
  {
    public List<TX> Parse(byte[] payload)
    {
      var bitcoinTXs = new List<TX>();

      int startIndex = 0;
      while (startIndex < payload.Length)
      {
        bitcoinTXs.Add(TX.Parse(payload, ref startIndex));
      }

      return bitcoinTXs;
    }

    public UInt256 ComputeMerkleRootHash(List<TX> bitcoinTXs)
    {
      const int HASH_BYTE_SIZE = 32;

      List<byte[]> merkleList = bitcoinTXs.Select(tx =>
      {
        return Hashing.SHA256d(tx.GetBytes());
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
          merkleListNext.Add(Hashing.SHA256d(leafPairHashesConcat));
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


    public void ValidatePayload(byte[] payload, UInt256 merkleRoot)
    {
    }
  }
}
