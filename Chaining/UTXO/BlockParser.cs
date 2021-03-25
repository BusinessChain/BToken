using System;
using System.Collections.Generic;
using System.Linq;
using System.Diagnostics;
using System.Security.Cryptography;

namespace BToken.Chaining
{
  partial class UTXOTable
  {
    public class BlockParser
    {
      public const int COUNT_HEADER_BYTES = 80;

      public byte[] Buffer;
      public int IndexBuffer;
            
      public List<Block> Blocks = new List<Block>();
          
      SHA256 SHA256 = SHA256.Create();

      public Stopwatch StopwatchInsertion = new Stopwatch();
      public Stopwatch StopwatchParse = new Stopwatch();
                 

      public void ClearPayloadData()
      {
        IndexBuffer = 0;
      }

      
      public Block ParseBlock(byte[] buffer)
      {
        int startIndex = 0;

        return ParseBlock(
          buffer, 
          ref startIndex);
      }

      public Block ParseBlock(
        byte[] buffer,
        ref int startIndex)
      {
        Buffer = buffer;
        IndexBuffer = startIndex;

        Header header = ParseHeader(
          Buffer,
          ref IndexBuffer);

        List<TX> tXs = ParseTXs(
          header.MerkleRoot);

        startIndex = IndexBuffer;
        
        return new Block(
          Buffer,
          header,
          tXs);
      }

      public Header ParseHeader(
        byte[] buffer,
        ref int index)
      {
        byte[] hash =
          SHA256.ComputeHash(
            SHA256.ComputeHash(
              buffer,
              index,
              COUNT_HEADER_BYTES));

        uint version = BitConverter.ToUInt32(buffer, index);
        index += 4;

        byte[] previousHeaderHash = new byte[32];
        Array.Copy(buffer, index, previousHeaderHash, 0, 32);
        index += 32;

        byte[] merkleRootHash = new byte[32];
        Array.Copy(buffer, index, merkleRootHash, 0, 32);
        index += 32;

        uint unixTimeSeconds = BitConverter.ToUInt32(
          buffer, index);
        index += 4;

        const long MAX_FUTURE_TIME_SECONDS = 2 * 60 * 60;
        if (unixTimeSeconds >
          (DateTimeOffset.UtcNow.ToUnixTimeSeconds() +
          MAX_FUTURE_TIME_SECONDS))
        {
          throw new ProtocolException(
            string.Format("Timestamp premature {0}",
              new DateTime(unixTimeSeconds).Date));
        }

        uint nBits = BitConverter.ToUInt32(buffer, index);
        index += 4;

        if (hash.IsGreaterThan(nBits))
        {
          throw new ProtocolException(
            string.Format("header hash {0} greater than NBits {1}",
              hash.ToHexString(),
              nBits));
        }

        uint nonce = BitConverter.ToUInt32(buffer, index);
        index += 4;

        return new Header(
          hash,
          version,
          previousHeaderHash,
          merkleRootHash,
          unixTimeSeconds,
          nBits,
          nonce);
      }


      List<TX> ParseTXs(
        byte[] hashMerkleRoot)
      {
        List<TX> tXs = new List<TX>();

        int tXCount = VarInt.GetInt32(
          Buffer,
          ref IndexBuffer);

        if (tXCount == 0)
        { }
        else if (tXCount == 1)
        {
          TX tX = ParseTX(isCoinbase: true);

          tXs.Add(tX);

          if (!tX.Hash.IsEqual(hashMerkleRoot))
          {
            throw new ProtocolException(
              "Payload merkle root corrupted");
          }
        }
        else
        {
          int tXsLengthMod2 = tXCount & 1;
          var merkleList = new byte[tXCount + tXsLengthMod2][];

          TX tX = ParseTX(true);
          tXs.Add(tX);

          merkleList[0] = tX.Hash;

          for (int t = 1; t < tXCount; t += 1)
          {
            tX = ParseTX(false);
            tXs.Add(tX);

            merkleList[t] = tX.Hash;
          }

          if (tXsLengthMod2 != 0)
          {
            merkleList[tXCount] = merkleList[tXCount - 1];
          }

          if (!GetRoot(merkleList).IsEqual(hashMerkleRoot))
          {
            throw new ProtocolException(
              "Payload hash unequal with merkle root.");
          }
        }

        return tXs;
      }

      TX ParseTX(bool isCoinbase)
      {
        TX tX = new TX();

        try
        {
          int tXStartIndex = IndexBuffer;

          IndexBuffer += 4; // BYTE_LENGTH_VERSION

          bool isWitnessFlagPresent = Buffer[IndexBuffer] == 0x00;
          if (isWitnessFlagPresent)
          {
            throw new NotImplementedException(
              "Parsing of segwit txs not implemented");
            //BufferIndex += 2;
          }

          int countInputs = VarInt.GetInt32(
            Buffer, ref IndexBuffer);

          if (isCoinbase)
          {
            new TXInput(Buffer, ref IndexBuffer);
          }
          else
          {
            for (int i = 0; i < countInputs; i += 1)
            {
              tX.TXInputs.Add(
                new TXInput(
                  Buffer, 
                  ref IndexBuffer));
            }
          }

          int countTXOutputs = VarInt.GetInt32(
            Buffer,
            ref IndexBuffer);

          for (int i = 0; i < countTXOutputs; i += 1)
          {
            tX.TXOutputs.Add(
              new TXOutput(
                Buffer,
                ref IndexBuffer));
          }

          //if (isWitnessFlagPresent)
          //{
          //var witnesses = new TXWitness[countInputs];
          //for (int i = 0; i < countInputs; i += 1)
          //{
          //  witnesses[i] = TXWitness.Parse(Buffer, ref BufferIndex);
          //}
          //}

          IndexBuffer += 4; //BYTE_LENGTH_LOCK_TIME

          tX.Hash = SHA256.ComputeHash(
           SHA256.ComputeHash(
             Buffer,
             tXStartIndex,
             IndexBuffer - tXStartIndex));

          tX.TXIDShort = BitConverter.ToInt32(tX.Hash, 0);

          int lengthUTXOBits = 
            COUNT_NON_OUTPUT_BITS + countTXOutputs;
          
          return tX;
        }
        catch (ArgumentOutOfRangeException)
        {
          throw new ProtocolException(
            "ArgumentOutOfRangeException thrown in ParseTX.");
        }
      }

      byte[] GetRoot(byte[][] merkleList)
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
        byte[] leafPair = new byte[2 * HASH_BYTE_SIZE];

        for (int i = 0; i < merkleIndex; i++)
        {
          int i2 = i << 1;
          merkleList[i2].CopyTo(leafPair, 0);
          merkleList[i2 + 1].CopyTo(leafPair, HASH_BYTE_SIZE);

          merkleList[i] = 
            SHA256.ComputeHash(
              SHA256.ComputeHash(leafPair));
        }
      }
    }
  }
}
