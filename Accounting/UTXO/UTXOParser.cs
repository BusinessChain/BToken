using System;
using System.Collections.Generic;
using System.Linq;

using BToken.Hashing;

namespace BToken.Accounting
{
  public partial class UTXO
  {
    class UTXOParser
    {
      public List<TX> Parse(byte[] payload)
      {
        var tXs = new List<TX>();

        int startIndex = 0;
        while (startIndex < payload.Length)
        {
          UInt32 version = BitConverter.ToUInt32(payload, startIndex);

          TX tX = TX.Parse(payload, ref startIndex);
          tXs.Add(tX);
        }

        return tXs;
      }

      public UInt256 ComputeMerkleRootHash(List<TX> tXs, out List<byte[]> tXHashes)
      {
        const int HASH_BYTE_SIZE = 32;
        tXHashes = new List<byte[]>();

        for (int t = 0; t < tXs.Count; t++)
        {
          tXHashes.Add(SHA256d.Compute(tXs[t].GetBytes()));
        }

        return new UInt256(GetRoot(tXHashes, HASH_BYTE_SIZE));
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
            merkleListNext.Add(SHA256d.Compute(leafPairHashesConcat));
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

    }
  }
}
