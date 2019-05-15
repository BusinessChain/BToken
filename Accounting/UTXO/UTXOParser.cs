using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Security.Cryptography;

namespace BToken.Accounting
{
  public partial class UTXO
  {
    static class UTXOParser
    {
      public static TX[] Parse(
      byte[] buffer,
      ref int bufferIndex,
      SHA256 sHA256Generator,
      int indexMerkleRoot)
      {
        int tXCount = VarInt.GetInt32(buffer, ref bufferIndex);
        TX[] tXs = new TX[tXCount];

        if (tXs.Length == 1)
        {
          tXs[0] = TX.Parse(buffer, ref bufferIndex, sHA256Generator);

          if (!tXs[0].Hash.IsEqual(buffer, indexMerkleRoot))
          {
            throw new UTXOException("Payload corrupted.");
          }

          return tXs;
        }

        int tXsLengthMod2 = tXs.Length & 1;

        var merkleList = new byte[tXs.Length + tXsLengthMod2][];

        for (int t = 0; t < tXs.Length; t++)
        {
          tXs[t] = TX.Parse(buffer, ref bufferIndex, sHA256Generator);
          merkleList[t] = tXs[t].Hash;
        }

        if (tXsLengthMod2 != 0)
        {
          merkleList[tXs.Length] = merkleList[tXs.Length - 1];
        }

        if (!GetRoot(merkleList, sHA256Generator).IsEqual(buffer, indexMerkleRoot))
        {
          throw new UTXOException("Payload corrupted.");
        }

        return tXs;
      }

      static byte[] GetRoot(
        byte[][] merkleList,
        SHA256 sHA256Generator)
      {
        int merkleIndex = merkleList.Length;

        while (true)
        {
          merkleIndex >>= 1;

          if (merkleIndex == 1)
          {
            return ComputeNextMerkleList(merkleList, merkleIndex, sHA256Generator)[0];
          }

          merkleList = ComputeNextMerkleList(merkleList, merkleIndex, sHA256Generator);

          if ((merkleIndex & 1) != 0)
          {
            merkleList[merkleIndex] = merkleList[merkleIndex - 1];
            merkleIndex += 1;
          }
        }

      }

      static byte[][] ComputeNextMerkleList(
        byte[][] merkleList,
        int merkleIndex,
        SHA256 sHA256Generator)
      {
        byte[] leafPair = new byte[TWICE_HASH_BYTE_SIZE];

        for (int i = 0; i < merkleIndex; i++)
        {
          int i2 = i << 1;
          merkleList[i2].CopyTo(leafPair, 0);
          merkleList[i2 + 1].CopyTo(leafPair, HASH_BYTE_SIZE);

          merkleList[i] = sHA256Generator.ComputeHash(
            sHA256Generator.ComputeHash(
              leafPair));
        }

        return merkleList;
      }
    }
  }
}
