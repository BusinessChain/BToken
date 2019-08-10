using System;
using System.Linq;
using System.Security.Cryptography;

using BToken.Chaining;

namespace BToken.Accounting
{
  public partial class UTXO
  {
    class UTXOParser
    {

      public const int COUNT_HEADER_BYTES = 80;
      const int BYTE_LENGTH_VERSION = 4;
      const int BYTE_LENGTH_OUTPUT_VALUE = 8;
      const int BYTE_LENGTH_LOCK_TIME = 4;
      const int OFFSET_INDEX_MERKLE_ROOT = 36;
      const int TWICE_HASH_BYTE_SIZE = HASH_BYTE_SIZE << 1;

      UTXO UTXO;

      int BatchIndex;
      byte[] Buffer;
      int BufferIndex;
      int MerkleIndex;
      byte[] HeaderHash;
      int TXCount;

      SHA256 SHA256;

      UTXOBatch Batch;
      
      public Headerchain.ChainHeader Header;


      public UTXOParser(UTXO uTXO)
      {
        UTXO = uTXO;
        SHA256 = SHA256.Create();
      }
      
      public UTXOBatch ParseBatch(byte[] buffer, int batchIndex)
      {
        BatchIndex = batchIndex;

        Batch = new UTXOBatch()
        {
          BatchIndex = BatchIndex
        };

        Buffer = buffer;
        BufferIndex = 0;

        HeaderHash =
          SHA256.ComputeHash(
            SHA256.ComputeHash(
              Buffer,
              BufferIndex,
              COUNT_HEADER_BYTES));
        
        BufferIndex += COUNT_HEADER_BYTES;
        TXCount = VarInt.GetInt32(buffer, ref BufferIndex);

        Header = UTXO.Headerchain.ReadHeader(HeaderHash, SHA256);
        Batch.HeaderPrevious = Header.HeaderPrevious;

        ParseBlock(OFFSET_INDEX_MERKLE_ROOT);
        Batch.BlockCount += 1;

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
            Header.GetHeaderHash(SHA256));

          ParseBlock(merkleRootIndex);
          Batch.BlockCount += 1;
        }

        Batch.HeaderLast = Header;
        Batch.ConvertTablesToArrays();
                     
        return Batch;
      }
      
      static void ValidateHeaderHash(
        byte[] headerHash,
        byte[] headerHashValidator)
      {
        if (!headerHash.IsEqual(headerHashValidator))
        {
          throw new UTXOException(
            string.Format("Unexpected header hash {0}, \nexpected {1}",
            headerHash.ToHexString(),
            headerHashValidator.ToHexString()));
        }
      }

      public static Block ParseBlockHeader(
        byte[] buffer,
        Headerchain.ChainHeader header,
        byte[] headerHash,
        SHA256 sHA256)
      {
        int bufferIndex = 0;

        byte[] headerHashParsed =
          sHA256.ComputeHash(
            sHA256.ComputeHash(
              buffer,
              bufferIndex,
              COUNT_HEADER_BYTES));

        bufferIndex += COUNT_HEADER_BYTES;
        int tXCount = VarInt.GetInt32(buffer, ref bufferIndex);

        ValidateHeaderHash(
          headerHashParsed,
          headerHash);

        return new Block(
          buffer,
          bufferIndex,
          header,
          headerHash,
          tXCount);
      }

      public void ParseBatch(UTXOBatch batch)
      {
        Batch = batch;

        BatchIndex = Batch.BatchIndex;

        batch.StopwatchParse.Start();
        foreach (Block block in batch.Blocks)
        {
          LoadBlock(block);
          ParseBlock(OFFSET_INDEX_MERKLE_ROOT);
          Batch.BlockCount += 1;
        }

        Batch.ConvertTablesToArrays();

        batch.HeaderPrevious = batch.Blocks[0].Header.HeaderPrevious;
        batch.HeaderLast = batch.Blocks.Last().Header;
        
        batch.StopwatchParse.Stop();
      }
      void LoadBlock(Block block)
      {
        Buffer = block.Buffer;
        BufferIndex = block.BufferIndex;
        MerkleIndex = block.BufferIndex + OFFSET_INDEX_MERKLE_ROOT;
        HeaderHash = block.HeaderHash;
        TXCount = block.TXCount;
      }

      void ParseBlock(int merkleRootIndex)
      {
        Batch.StopwatchParse.Start();

        if (TXCount == 1)
        {
          byte[] tXHash = ParseTX(true);

          if (!tXHash.IsEqual(Buffer, merkleRootIndex))
          {
            throw new UTXOException("Payload merkle root corrupted");
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

        if (!GetRoot(merkleList, SHA256).IsEqual(Buffer, merkleRootIndex))
        {
          throw new ChainException("Payload merkle root corrupted.");
        }

        Batch.StopwatchParse.Stop();

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
            BufferIndex += 2;
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
              
              if (
               !(Batch.TableUInt32.TrySpend(input) ||
               Batch.TableULong64.TrySpend(input) ||
               Batch.TableUInt32Array.TrySpend(input)))
              {
                Batch.Inputs.Add(input);
              }
            }
          }

          int countTXOutputs = VarInt.GetInt32(Buffer, ref BufferIndex);
          for (int i = 0; i < countTXOutputs; i += 1)
          {
            BufferIndex += BYTE_LENGTH_OUTPUT_VALUE;
            int lengthLockingScript = VarInt.GetInt32(Buffer, ref BufferIndex);
            BufferIndex += lengthLockingScript;
          }

          if (isWitnessFlagPresent)
          {
            var witnesses = new TXWitness[countInputs];
            for (int i = 0; i < countInputs; i += 1)
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

          int lengthUTXOBits = CountNonOutputBits + countTXOutputs;
          
          if (COUNT_INTEGER_BITS >= lengthUTXOBits)
          {
            Batch.TableUInt32.ParseUTXO(
              BatchIndex,
              lengthUTXOBits,
              tXHash);
          }
          else if (COUNT_LONG_BITS >= lengthUTXOBits)
          {
            Batch.TableULong64.ParseUTXO(
              BatchIndex,
              lengthUTXOBits,
              tXHash);
          }
          else
          {
            Batch.TableUInt32Array.ParseUTXO(
              BatchIndex,
              lengthUTXOBits,
              tXHash);
          }

          return tXHash;
        }
        catch (ArgumentOutOfRangeException)
        {
          throw new ChainException();
        }
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
