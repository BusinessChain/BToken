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

      byte[] Buffer;
      int BatchIndex;
      byte[] HeaderHash;
      int TXCount;

      public UTXOParserData UTXOParserData;
      Headerchain.ChainHeader ChainHeader;

      int BufferIndex = 0;
      SHA256 SHA256 = SHA256.Create();


      public UTXOParser(UTXO uTXO)
      {
        UTXO = uTXO;
      }
      
      public UTXOBatch ParseBatch(byte[] buffer, int batchIndex)
      {
        int bufferIndex = 0;

        Block block = new Block(buffer, bufferIndex);

        ParseHeader(
          block,
          SHA256);

        Headerchain.ChainHeader chainHeader
          = UTXO.Headerchain.ReadHeader(block.HeaderHash, SHA256);

        UTXOBatch batch = new UTXOBatch()
        {
          BatchIndex = batchIndex,
        };

        batch.UTXOParserData.Add(
          ParseBlock(block, batch.BatchIndex));

        while (BufferIndex < Buffer.Length)
        {
          ParseHeader(
            block,
            SHA256);

          chainHeader = chainHeader.HeadersNext[0];

          ValidateHeaderHash(
            block.HeaderHash,
            chainHeader.GetHeaderHash(SHA256));

          batch.UTXOParserData.Add(
            ParseBlock(block, batch.BatchIndex));
        }

        return batch;
      }

      public static void ParseHeader(
        Block block, 
        SHA256 sHA256)
      {
        block.HeaderHash =
          sHA256.ComputeHash(
            sHA256.ComputeHash(
              block.Buffer,
              block.BufferIndex,
              COUNT_HEADER_BYTES));

        block.BufferIndex += COUNT_HEADER_BYTES;
        block.TXCount = VarInt.GetInt32(block.Buffer, ref block.BufferIndex);
      }

      public static void ValidateHeaderHash(byte[] headerHash, byte[] headerHashValidator)
      {
        if (!headerHash.IsEqual(headerHashValidator))
        {
          throw new UTXOException(
            string.Format("Unexpected header hash {0}, \nexpected {1}",
            headerHash.ToHexString(),
            headerHashValidator.ToHexString()));
        }
      }

      public void ParseBatch(UTXOBatch batch)
      {
        foreach (Block block in batch.Blocks)
        {
          UTXOParserData uTXOParserData = ParseBlock(block, batch.BatchIndex);
          batch.UTXOParserData.Add(uTXOParserData);
        }
      }
      public UTXOParserData ParseBlock(Block block, int batchIndex)
      {
        Buffer = block.Buffer;
        BufferIndex = block.BufferIndex;
        HeaderHash = block.HeaderHash;
        TXCount = block.TXCount;

        BatchIndex = batchIndex;
        UTXOParserData = new UTXOParserData(TXCount);

        if (TXCount == 1)
        {
          byte[] tXHash = ParseTX(0, true);

          if (!tXHash.IsEqual(Buffer, OFFSET_INDEX_MERKLE_ROOT))
          {
            throw new UTXOException("Payload merkle root corrupted");
          }

          return UTXOParserData;
        }

        int tXsLengthMod2 = TXCount & 1;
        var merkleList = new byte[TXCount + tXsLengthMod2][];

        merkleList[0] = ParseTX(0, true);

        for (int t = 1; t < TXCount; t += 1)
        {
          merkleList[t] = ParseTX(t, false);
        }

        if (tXsLengthMod2 != 0)
        {
          merkleList[TXCount] = merkleList[TXCount - 1];
        }

        if (!GetRoot(merkleList, SHA256).IsEqual(Buffer, OFFSET_INDEX_MERKLE_ROOT))
        {
          throw new UTXOException("Payload merkle root corrupted.");
        }

        return UTXOParserData;
      }

      byte[] ParseTX(
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
          UTXOParserData.InputsPerTX[tXIndex] = new TXInput[0];
          new TXInput(Buffer, ref BufferIndex);
        }
        else
        {
          UTXOParserData.InputsPerTX[tXIndex] = new TXInput[countTXInputs];
          for (int i = 0; i < countTXInputs; i += 1)
          {
            UTXOParserData.InputsPerTX[tXIndex][i] = new TXInput(Buffer, ref BufferIndex);
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
            HeaderHash,
            lengthUTXOBits,
            out UTXOItem uTXOItem))
          {
            UTXOParserData.PushUTXOItem(c, uTXOItem);

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
