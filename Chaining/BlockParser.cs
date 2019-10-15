using System;
using System.Linq;
using System.Security.Cryptography;
using System.Diagnostics;

namespace BToken.Chaining
{
  partial class UTXOTable
  {
    class BlockParser
    {
      const int COUNT_HEADER_BYTES = 80;

      const int BYTE_LENGTH_VERSION = 4;
      const int BYTE_LENGTH_OUTPUT_VALUE = 8;
      const int BYTE_LENGTH_LOCK_TIME = 4;
      const int OFFSET_INDEX_MERKLE_ROOT = 36;
      const int TWICE_HASH_BYTE_SIZE = HASH_BYTE_SIZE << 1;

      int BatchIndex;
      byte[] Buffer;
      int BufferIndex;
      byte[] HeaderHash;
      int TXCount;

      SHA256 SHA256;

      Headerchain Chain;
      public Header Header;

      BlockBatchContainer BlockBatchContainer;


      public BlockParser(Headerchain chain)
      {
        Chain = chain;
        SHA256 = SHA256.Create();
      }

      public void Parse(BlockBatchContainer blockBatchContainer)
      {
        BlockBatchContainer = blockBatchContainer;

        BatchIndex = blockBatchContainer.Index;

        Buffer = blockBatchContainer.Buffer;
        BufferIndex = 0;

        HeaderHash =
          SHA256.ComputeHash(
            SHA256.ComputeHash(
              Buffer,
              BufferIndex,
              COUNT_HEADER_BYTES));

        BufferIndex += COUNT_HEADER_BYTES;
        TXCount = VarInt.GetInt32(Buffer, ref BufferIndex);

        if (blockBatchContainer.Header == null)
        {
          Header = Chain.ReadHeader(HeaderHash, SHA256);
        }
        else
        {
          Header = blockBatchContainer.Header;

          ValidateHeaderHash(
            HeaderHash,
            Header.HeaderHash);
        }

        blockBatchContainer.HeaderPrevious = Header.HeaderPrevious;

        ParseBlock(OFFSET_INDEX_MERKLE_ROOT);
        blockBatchContainer.BlockCount += 1;
        blockBatchContainer.CountItems += TXCount;

        while (BufferIndex < Buffer.Length)
        {
          HeaderHash =
            SHA256.ComputeHash(
              SHA256.ComputeHash(
                Buffer,
                BufferIndex,
                COUNT_HEADER_BYTES));

          int merkleRootIndex = BufferIndex + OFFSET_INDEX_MERKLE_ROOT;
          BufferIndex += COUNT_HEADER_BYTES;
          TXCount = VarInt.GetInt32(Buffer, ref BufferIndex);

          Header = Header.HeadersNext[0];

          ValidateHeaderHash(
            HeaderHash,
            Header.HeaderHash);

          ParseBlock(merkleRootIndex);
          blockBatchContainer.BlockCount += 1;
          blockBatchContainer.CountItems += TXCount;
        }

        blockBatchContainer.Header = Header;
        blockBatchContainer.ConvertTablesToArrays();
      }

      static void ValidateHeaderHash(
        byte[] headerHash,
        byte[] headerHashValidator)
      {
        if (!headerHash.IsEqual(headerHashValidator))
        {
          throw new ChainException(
            string.Format("Unexpected header hash {0}, \nexpected {1}",
            headerHash.ToHexString(),
            headerHashValidator.ToHexString()));
        }
      }

      void ParseBlock(int merkleRootIndex)
      {
        if (TXCount == 1)
        {
          byte[] tXHash = ParseTX(true);

          if (!tXHash.IsEqual(Buffer, merkleRootIndex))
          {
            throw new ChainException("Payload merkle root corrupted");
          }

          return;
        }

        int tXsLengthMod2 = TXCount & 1;
        var merkleList = new byte[TXCount + tXsLengthMod2][];

        merkleList[0] = ParseTX(true);

        for (int t = 1; t < TXCount; t += 1)
        {
          merkleList[t] = ParseTX(false);
        }

        if (tXsLengthMod2 != 0)
        {
          merkleList[TXCount] = merkleList[TXCount - 1];
        }

        if (!GetRoot(merkleList).IsEqual(Buffer, merkleRootIndex))
        {
          throw new ChainException("Payload merkle root corrupted.");
        }

        return;
      }

      byte[] ParseTX(bool isCoinbase)
      {
        try
        {
          int tXStartIndex = BufferIndex;

          BufferIndex += BYTE_LENGTH_VERSION;

          bool isWitnessFlagPresent = Buffer[BufferIndex] == 0x00;
          if (isWitnessFlagPresent)
          {
            throw new NotImplementedException("Parsing of segwit txs not implemented");
            //BufferIndex += 2;
          }

          int countInputs = VarInt.GetInt32(Buffer, ref BufferIndex);

          if (isCoinbase)
          {
            new TXInput(Buffer, ref BufferIndex);
          }
          else
          {
            for (int i = 0; i < countInputs; i += 1)
            {
              TXInput input = new TXInput(Buffer, ref BufferIndex);

              BlockBatchContainer.AddInput(input);
            }
          }

          int countTXOutputs = VarInt.GetInt32(Buffer, ref BufferIndex);

          for (int i = 0; i < countTXOutputs; i += 1)
          {
            BufferIndex += BYTE_LENGTH_OUTPUT_VALUE;
            int lengthLockingScript = VarInt.GetInt32(Buffer, ref BufferIndex);
            BufferIndex += lengthLockingScript;
          }

          //if (isWitnessFlagPresent)
          //{
          //var witnesses = new TXWitness[countInputs];
          //for (int i = 0; i < countInputs; i += 1)
          //{
          //  witnesses[i] = TXWitness.Parse(Buffer, ref BufferIndex);
          //}
          //}

          BufferIndex += BYTE_LENGTH_LOCK_TIME;

          int tXLength = BufferIndex - tXStartIndex;

          byte[] tXHash = SHA256.ComputeHash(
           SHA256.ComputeHash(
             Buffer,
             tXStartIndex,
             tXLength));

          BlockBatchContainer.AddOutput(
            tXHash, 
            BatchIndex, 
            countTXOutputs);

          return tXHash;
        }
        catch (ArgumentOutOfRangeException)
        {
          throw new ChainException();
        }
      }

      byte[] GetRoot(
        byte[][] merkleList)
      {
        int merkleIndex = merkleList.Length;

        while (true)
        {
          merkleIndex >>= 1;

          if (merkleIndex == 1)
          {
            ComputeNextMerkleList(merkleList, merkleIndex);
            return merkleList[0];
          }

          ComputeNextMerkleList(merkleList, merkleIndex);

          if ((merkleIndex & 1) != 0)
          {
            merkleList[merkleIndex] = merkleList[merkleIndex - 1];
            merkleIndex += 1;
          }
        }
      }

      void ComputeNextMerkleList(
        byte[][] merkleList,
        int merkleIndex)
      {
        byte[] leafPair = new byte[TWICE_HASH_BYTE_SIZE];

        for (int i = 0; i < merkleIndex; i++)
        {
          int i2 = i << 1;
          merkleList[i2].CopyTo(leafPair, 0);
          merkleList[i2 + 1].CopyTo(leafPair, HASH_BYTE_SIZE);

          merkleList[i] = SHA256.ComputeHash(
            SHA256.ComputeHash(leafPair));
        }
      }
    }
  }
}
