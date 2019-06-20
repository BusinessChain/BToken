using System;
using System.Security.Cryptography;

using BToken.Chaining;

namespace BToken.Accounting
{
  public partial class UTXO
  {
    const int COUNT_HEADER_BYTES = 80;
    const int BYTE_LENGTH_VERSION = 4;
    const int BYTE_LENGTH_OUTPUT_VALUE = 8;
    const int BYTE_LENGTH_LOCK_TIME = 4;
    const int OFFSET_INDEX_MERKLE_ROOT = 36;
    const int TWICE_HASH_BYTE_SIZE = HASH_BYTE_SIZE << 1;
    
    void ParseBatch(UTXOBatch batch)
    {
      batch.StopwatchParse.Start();

      while (batch.BufferIndex < batch.Buffer.Length)
      {
        try
        {
          byte[] headerHash =
            batch.SHA256.ComputeHash(
              batch.SHA256.ComputeHash(
                batch.Buffer,
                batch.BufferIndex,
                COUNT_HEADER_BYTES));

          ValidateHeaderHash(headerHash, batch);

          int startIndexBlock = batch.BufferIndex;
          int indexMerkleRoot = batch.BufferIndex + OFFSET_INDEX_MERKLE_ROOT;

          batch.BufferIndex += COUNT_HEADER_BYTES;

          int tXCount = VarInt.GetInt32(batch.Buffer, ref batch.BufferIndex);

          var block = new Block(
            batch.Buffer,
            startIndexBlock,
            batch.BufferIndex,
            headerHash,
            tXCount);

          batch.Blocks.Add(block);

          if (tXCount == 1)
          {
            byte[] tXHash = ParseTX(
              block,
              batch,
              tXIndex: 0,
              isCoinbase: true);

            if (!tXHash.IsEqual(batch.Buffer, indexMerkleRoot))
            {
              throw new UTXOException(
                string.Format("Payload corrupted. BatchIndex {0}, BufferIndex: {1}",
                batch.BatchIndex,
                batch.BufferIndex));
            }

            continue;
          }

          int tXsLengthMod2 = tXCount & 1;
          var merkleList = new byte[tXCount + tXsLengthMod2][];

          merkleList[0] = ParseTX(
            block,
            batch,
            tXIndex: 0,
            isCoinbase: true);

          for (int t = 1; t < tXCount; t += 1)
          {
            merkleList[t] = ParseTX(
              block,
              batch,
              tXIndex: t,
              isCoinbase: false);
          }

          if (tXsLengthMod2 != 0)
          {
            merkleList[tXCount] = merkleList[tXCount - 1];
          }

          if (!GetRoot(merkleList, batch.SHA256)
            .IsEqual(batch.Buffer, indexMerkleRoot))
          {
            throw new UTXOException(
              string.Format("Payload corrupted. batchIndex {0}, bufferIndex: {1}",
              batch.BatchIndex,
              batch.BufferIndex));
          }
        }
        catch (Exception ex)
        {
          Console.WriteLine(ex.Message);
        }
      }

      batch.StopwatchParse.Stop();
    }
    
    void ValidateHeaderHash(
      byte[] headerHash,
      UTXOBatch batch)
    {
      if (batch.ChainHeader == null)
      {
        batch.ChainHeader = Headerchain.ReadHeader(
          headerHash, 
          batch.SHA256);

        batch.HeaderHashPrevious = batch.ChainHeader.NetworkHeader.HashPrevious;
      }

      byte[] headerHashValidator;

      if (batch.ChainHeader.HeadersNext == null)
      {
        headerHashValidator = batch.ChainHeader.GetHeaderHash(batch.SHA256);
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
      UTXOBatch batch,
      int tXIndex,
      bool isCoinbase)
    {
      int tXStartIndex = batch.BufferIndex;

      batch.BufferIndex += BYTE_LENGTH_VERSION;

      bool isWitnessFlagPresent = batch.Buffer[batch.BufferIndex] == 0x00;
      if (isWitnessFlagPresent)
      {
        batch.BufferIndex += 2;
      }

      int countTXInputs = VarInt.GetInt32(batch.Buffer, ref batch.BufferIndex);
      if (isCoinbase)
      {
        block.InputsPerTX[tXIndex] = new TXInput[0];
        new TXInput(batch.Buffer, ref batch.BufferIndex);
      }
      else
      {
        block.InputsPerTX[tXIndex] = new TXInput[countTXInputs];
        for (int i = 0; i < countTXInputs; i += 1)
        {
          block.InputsPerTX[tXIndex][i] = new TXInput(batch.Buffer, ref batch.BufferIndex);
        }
      }

      int countTXOutputs = VarInt.GetInt32(batch.Buffer, ref batch.BufferIndex);
      for (int i = 0; i < countTXOutputs; i += 1)
      {
        batch.BufferIndex += BYTE_LENGTH_OUTPUT_VALUE;
        int lengthLockingScript = VarInt.GetInt32(batch.Buffer, ref batch.BufferIndex);
        batch.BufferIndex += lengthLockingScript;
      }

      int lengthUTXOBits = CountNonOutputBits + countTXOutputs;
      
      for (int c = 0; c < Tables.Length; c += 1)
      {
        if (Tables[c].TryParseUTXO(
          batch.BatchIndex,
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
              witnesses[i] = TXWitness.Parse(batch.Buffer, ref batch.BufferIndex);
            }
          }

          batch.BufferIndex += BYTE_LENGTH_LOCK_TIME;

          int tXLength = batch.BufferIndex - tXStartIndex;
          block.Length += tXLength;

          byte[] tXHash = batch.SHA256.ComputeHash(
           batch.SHA256.ComputeHash(
             batch.Buffer,
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
