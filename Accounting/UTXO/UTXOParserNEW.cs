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
    class UTXOParser
    {
      UTXO UTXO;

      byte[] Buffer;
      int BufferIndex;
      SHA256 SHA256;


      public UTXOParser(UTXO uTXO)
      {
        UTXO = uTXO;
      }
      
      public Block ParseBlock(
        byte[] buffer,
        int bufferIndex,
        byte[] headerHashExpected,
        SHA256 sHA256)
      {
        Buffer = buffer;
        BufferIndex = bufferIndex;
        SHA256 = sHA256;

        try
        {
          byte[] headerHash =
            sHA256.ComputeHash(
              sHA256.ComputeHash(
                buffer,
                bufferIndex,
                COUNT_HEADER_BYTES));

          if (!headerHash.IsEqual(headerHashExpected))
          {
            throw new UTXOException(string.Format("Unexpected header hash {0}, \nexpected {1}",
              headerHash.ToHexString(),
              headerHashExpected.ToHexString()));
          }

          int startIndexBlock = bufferIndex;
          int indexMerkleRoot = bufferIndex + OFFSET_INDEX_MERKLE_ROOT;

          bufferIndex += COUNT_HEADER_BYTES;

          int tXCount = VarInt.GetInt32(buffer, ref bufferIndex);

          var block = new Block(
            buffer,
            startIndexBlock,
            bufferIndex,
            headerHash,
            tXCount);

          if (tXCount == 1)
          {
            byte[] tXHash = ParseTX(
              block,
              tXIndex: 0,
              isCoinbase: true);

            if (!tXHash.IsEqual(buffer, indexMerkleRoot))
            {
              throw new UTXOException(
                string.Format("Payload corrupted. BufferIndex: {0}",
                bufferIndex));
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

          if (!GetRoot(merkleList, sHA256)
            .IsEqual(buffer, indexMerkleRoot))
          {
            throw new UTXOException(
              string.Format("Payload corrupted. bufferIndex: {0}",
              bufferIndex));
          }

          return block;
        }
        catch (Exception ex)
        {
          Console.WriteLine(ex.Message);
          throw ex;
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
            block.Length += tXLength;

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
    }
  }
}
