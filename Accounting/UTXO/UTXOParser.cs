using System;
using System.Collections.Generic;
using System.Security.Cryptography;

using BToken.Chaining;

namespace BToken.Accounting
{
  public partial class UTXO
  {
    class UTXOParser
    {
      const int COUNT_HEADER_BYTES = 80;
      const int BYTE_LENGTH_VERSION = 4;
      const int BYTE_LENGTH_OUTPUT_VALUE = 8;
      const int BYTE_LENGTH_LOCK_TIME = 4;
      const int OFFSET_INDEX_MERKLE_ROOT = 36;
      const int TWICE_HASH_BYTE_SIZE = HASH_BYTE_SIZE << 1;

      UTXO UTXO;

      UTXOBatch Batch;
      byte[] Buffer;
      int BatchIndex;

      Headerchain.ChainHeader ChainHeader;

      int BufferIndex = 0;
      SHA256 SHA256 = SHA256.Create();


      public UTXOParser(UTXO uTXO)
      {
        UTXO = uTXO;
      }
      
      public void Load(UTXOBatch batch)
      {
        Batch = batch;
        Buffer = batch.Buffer;
      }

      
      public void ParseBatch()
      {
        while (BufferIndex < Buffer.Length)
        {
          ParseHeader(out int indexMerkleRoot, out byte[] headerHash);

          Block block = ParseBlock(headerHash, indexMerkleRoot);

          Batch.Blocks.Add(block);
        }
      }
      public Block ParseBlock(byte[] headerHash, int indexMerkleRoot)
      {
        int tXCount = VarInt.GetInt32(Buffer, ref BufferIndex);
        var block = new Block(headerHash, tXCount);

        if (tXCount == 1)
        {
          byte[] tXHash = ParseTX(
            block,
            tXIndex: 0,
            isCoinbase: true);

          if (!tXHash.IsEqual(Buffer, indexMerkleRoot))
          {
            throw new UTXOException(
              string.Format("Payload corrupted. BatchIndex {0}, BufferIndex: {1}",
              BatchIndex,
              BufferIndex));
          }

          return block;
        }

        int tXsLengthMod2 = tXCount & 1;
        var merkleList = new byte[tXCount + tXsLengthMod2][];

        merkleList[0] = ParseTX(
          block,
          tXIndex: 0,
          isCoinbase: true);

        for (int t = 1; t < tXCount; t += 1)
        {
          merkleList[t] = ParseTX(
            block,
            tXIndex: t,
            isCoinbase: false);
        }

        if (tXsLengthMod2 != 0)
        {
          merkleList[tXCount] = merkleList[tXCount - 1];
        }

        if (!GetRoot(merkleList, SHA256).IsEqual(Buffer, indexMerkleRoot))
        {
          throw new UTXOException(
            string.Format("Payload corrupted. batchIndex {0}, bufferIndex: {1}",
            BatchIndex,
            BufferIndex));
        }

        return block;
      }

      public void ParseHeader(out int indexMerkleRoot, out byte[] headerHash)
      {
        headerHash =
          SHA256.ComputeHash(
            SHA256.ComputeHash(
              Buffer,
              BufferIndex,
              COUNT_HEADER_BYTES));

        ValidateHeaderHash(headerHash);

        indexMerkleRoot = BufferIndex + OFFSET_INDEX_MERKLE_ROOT;
        BufferIndex += COUNT_HEADER_BYTES;
      }

      void ValidateHeaderHash(byte[] headerHash)
      {
        if (ChainHeader == null)
        {
          ChainHeader = UTXO.Headerchain.ReadHeader(headerHash, SHA256);
          return;
        }

        ChainHeader = ChainHeader.HeadersNext[0];

        byte[] headerHashValidator = ChainHeader.GetHeaderHash(SHA256);
        if (!headerHash.IsEqual(headerHashValidator))
        {
          throw new UTXOException(string.Format("Unexpected header hash {0}, \nexpected {1}",
            headerHash.ToHexString(),
            headerHashValidator.ToHexString()));
        }
      }

      byte[] ParseTX(
        Block block,
        int tXIndex,
        bool isCoinbase)
      {
        int tXStartIndex = BufferIndex;

        BufferIndex += BYTE_LENGTH_VERSION;

        bool isWitnessFlagPresent = Buffer[BufferIndex] == 0x00;
        if (isWitnessFlagPresent)
        {
          BufferIndex += 2;
        }

        int countTXInputs = VarInt.GetInt32(Buffer, ref BufferIndex);
        if (isCoinbase)
        {
          block.InputsPerTX[tXIndex] = new TXInput[0];
          new TXInput(Buffer, ref BufferIndex);
        }
        else
        {
          block.InputsPerTX[tXIndex] = new TXInput[countTXInputs];
          for (int i = 0; i < countTXInputs; i += 1)
          {
            block.InputsPerTX[tXIndex][i] = new TXInput(Buffer, ref BufferIndex);
          }
        }

        int countTXOutputs = VarInt.GetInt32(Buffer, ref BufferIndex);
        for (int i = 0; i < countTXOutputs; i += 1)
        {
          BufferIndex += BYTE_LENGTH_OUTPUT_VALUE;
          int lengthLockingScript = VarInt.GetInt32(Buffer, ref BufferIndex);
          BufferIndex += lengthLockingScript;
        }

        int lengthUTXOBits = CountNonOutputBits + countTXOutputs;

        for (int c = 0; c < UTXO.Tables.Length; c += 1)
        {
          if (UTXO.Tables[c].TryParseUTXO(
            BatchIndex,
            block.HeaderHash,
            lengthUTXOBits,
            out UTXOItem uTXOItem))
          {
            block.PushUTXOItem(c, uTXOItem);

            if (isWitnessFlagPresent)
            {
              var witnesses = new TXWitness[countTXInputs];
              for (int i = 0; i < countTXInputs; i += 1)
              {
                witnesses[i] = TXWitness.Parse(Buffer, ref BufferIndex);
              }
            }

            BufferIndex += BYTE_LENGTH_LOCK_TIME;

            int tXLength = BufferIndex - tXStartIndex;

            byte[] tXHash = SHA256.ComputeHash(
             SHA256.ComputeHash(
               Buffer,
               tXStartIndex,
               tXLength));

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
}
