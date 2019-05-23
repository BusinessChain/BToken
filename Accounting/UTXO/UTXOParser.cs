using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Security.Cryptography;

using BToken.Chaining;

namespace BToken.Accounting
{
  public partial class UTXO
  {
    void ParseBlock(
      UTXOBatch batch,
      ref int bufferIndex)
    {
      byte[] headerHash =
      batch.SHA256Generator.ComputeHash(
        batch.SHA256Generator.ComputeHash(
          batch.Buffer,
          bufferIndex,
          COUNT_HEADER_BYTES));

      ValidateHeaderHash(headerHash, batch);

      int indexMerkleRoot = bufferIndex + OFFSET_INDEX_MERKLE_ROOT;

      bufferIndex += COUNT_HEADER_BYTES;

      int tXCount = VarInt.GetInt32(batch.Buffer, ref bufferIndex);
      var block = new Block(tXCount, headerHash);
      batch.PushBlock(block);

      if (tXCount == 1)
      {
        byte[] tXHash = ParseTX(
          block,
          batch.Buffer,
          ref bufferIndex,
          headerHash,
          tXIndex: 0,
          isCoinbase: true,
          batch.SHA256Generator);

        if (!tXHash.IsEqual(batch.Buffer, indexMerkleRoot))
        {
          throw new UTXOException(
            string.Format("Payload corrupted. batchIndex {0}, bufferIndex: {1}",
            batch.BatchIndex,
            bufferIndex));
        }

        return;
      }

      int tXsLengthMod2 = tXCount & 1;
      var merkleList = new byte[tXCount + tXsLengthMod2][];

      merkleList[0] = ParseTX(
        block,
        batch.Buffer,
        ref bufferIndex,
        headerHash,
        tXIndex: 0,
        isCoinbase: true,
        batch.SHA256Generator);

      for (int t = 1; t < tXCount; t += 1)
      {
        merkleList[t] = ParseTX(
          block,
          batch.Buffer,
          ref bufferIndex,
          headerHash,
          tXIndex: t,
          isCoinbase: false,
          batch.SHA256Generator);
      }

      if (tXsLengthMod2 != 0)
      {
        merkleList[tXCount] = merkleList[tXCount - 1];
      }

      if (!GetRoot(merkleList, batch.SHA256Generator)
        .IsEqual(batch.Buffer, indexMerkleRoot))
      {
        throw new UTXOException(
          string.Format("Payload corrupted. batchIndex {0}, bufferIndex: {1}",
          batch.BatchIndex,
          bufferIndex));
      }
    }

    void ValidateHeaderHash(
      byte[] headerHash,
      UTXOBatch batch)
    {
      if (batch.ChainHeader == null)
      {
        batch.ChainHeader = Headerchain.ReadHeader(headerHash, batch.SHA256Generator);
      }

      byte[] headerHashValidator;

      if (batch.ChainHeader.HeadersNext == null)
      {
        headerHashValidator = batch.ChainHeader.GetHeaderHash(batch.SHA256Generator);
      }
      else
      {
        batch.ChainHeader = batch.ChainHeader.HeadersNext[0];
        headerHashValidator = batch.ChainHeader.NetworkHeader.HashPrevious;
      }

      if (!headerHashValidator.IsEqual(headerHash))
      {
        throw new UTXOException(string.Format("Unexpected header hash {0}, \nexpected {1}",
          headerHash.ToHexString(),
          headerHashValidator.ToHexString()));
      }
    }

    byte[] ParseTX(
      Block block,
      byte[] buffer,
      ref int bufferIndex,
      byte[] headerHash,
      int tXIndex,
      bool isCoinbase,
      SHA256 sHA256Generator)
    {
      int tXStartIndex = bufferIndex;

      bufferIndex += BYTE_LENGTH_VERSION;

      bool isWitnessFlagPresent = buffer[bufferIndex] == 0x00;
      if (isWitnessFlagPresent)
      {
        bufferIndex += 2;
      }

      int countTXInputs = VarInt.GetInt32(buffer, ref bufferIndex);
      if (isCoinbase)
      {
        block.InputsPerTX[tXIndex] = new TXInput[0];
        new TXInput(buffer, ref bufferIndex);
      }
      else
      {
        block.InputsPerTX[tXIndex] = new TXInput[countTXInputs];
        for (int i = 0; i < countTXInputs; i += 1)
        {
          block.InputsPerTX[tXIndex][i] = new TXInput(buffer, ref bufferIndex);
        }
      }

      int tXOutputsCount = VarInt.GetInt32(buffer, ref bufferIndex);
      for (int i = 0; i < tXOutputsCount; i += 1)
      {
        bufferIndex += BYTE_LENGTH_OUTPUT_VALUE;
        int lengthLockingScript = VarInt.GetInt32(buffer, ref bufferIndex);
        bufferIndex += lengthLockingScript;
      }
      int lengthUTXOBits = CountHeaderPlusCollisionBits + tXOutputsCount;

      for (int c = 0; c < Tables.Length; c += 1)
      {
        if (Tables[c].TryParseUTXO(
          headerHash,
          lengthUTXOBits,
          out UTXOItem uTXOItem))
        {
          block.PushUTXOItem(c, uTXOItem);

          if (isWitnessFlagPresent)
          {
            var witnesses = new TXWitness[countTXInputs];
            for (int i = 0; i < countTXInputs; i += 1)
            {
              witnesses[i] = TXWitness.Parse(buffer, ref bufferIndex);
            }
          }

          bufferIndex += BYTE_LENGTH_LOCK_TIME;

          byte[] tXHash = sHA256Generator.ComputeHash(
           sHA256Generator.ComputeHash(
             buffer,
             tXStartIndex,
             bufferIndex - tXStartIndex));
          
          uTXOItem.PrimaryKey = BitConverter.ToInt32(tXHash, 0);
          uTXOItem.Hash = tXHash;

          return tXHash;
        }
      }

      throw new UTXOException("UTXO could not be parsed by table modules.");
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
