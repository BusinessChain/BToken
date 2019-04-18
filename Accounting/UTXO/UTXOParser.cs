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
      public TX[] ParseBlock(byte[] buffer, ref int startIndex, int tXCount)
      {
        var tXs = new TX[tXCount];
        for (int i = 0; i < tXCount; i++)
        {
          UInt32 version = BitConverter.ToUInt32(buffer, startIndex);

          tXs[i] = TX.Parse(buffer, ref startIndex);
        }

        return tXs;
      }
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

      public UInt256 ComputeMerkleRootHash(TX[] tXs, out byte[][] tXHashes)
      {
        if (tXs.Length % 2 == 0)
        {
          tXHashes = new byte[tXs.Length][];

          for (int t = 0; t < tXs.Length; t++)
          {
            tXHashes[t] = SHA256d.Compute(tXs[t].GetBytes());
          }
        }
        else
        {
          tXHashes = new byte[tXs.Length + 1][];

          for (int t = 0; t < tXs.Length; t++)
          {
            tXHashes[t] = SHA256d.Compute(tXs[t].GetBytes());
          }

          tXHashes[tXHashes.Length - 1] = tXHashes[tXHashes.Length - 2];
        }

        return new UInt256(GetRoot(tXHashes));
      }
      byte[] GetRoot(byte[][] merkleList)
      {
        while (merkleList.Length > 1)
        {
          byte[][] merkleListNext;
          int lengthMerkleListNext = merkleList.Length / 2;
          if (lengthMerkleListNext % 2 == 0)
          {
            merkleListNext = ComputeNextMerkleList(merkleList, lengthMerkleListNext);
          }
          else
          {
            lengthMerkleListNext++;

            merkleListNext = ComputeNextMerkleList(merkleList, lengthMerkleListNext);

            merkleListNext[merkleListNext.Length - 1] = merkleListNext[merkleListNext.Length - 2];
          }

          merkleList = merkleListNext;
        }

        return merkleList[0];
      }
      byte[][] ComputeNextMerkleList(byte[][] merkleList, int lengthMerkleListNext)
      {
        var merkleListNext = new byte[lengthMerkleListNext][];

        for (int i = 0; i < merkleList[i].Length; i += 2)
        {
          const int HASH_BYTE_SIZE = 32;
          var leafPairHashesConcat = new byte[2 * HASH_BYTE_SIZE];
          merkleList[i].CopyTo(leafPairHashesConcat, 0);
          merkleList[i + 1].CopyTo(leafPairHashesConcat, HASH_BYTE_SIZE);

          merkleListNext[i / 2] = SHA256d.Compute(leafPairHashesConcat);
        }

        return merkleListNext;
      }
    }
  }
}
