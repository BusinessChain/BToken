using System;
using System.Collections.Generic;
using System.Linq;
using System.Diagnostics;
using System.Threading.Tasks;
using System.Security.Cryptography;

namespace BToken.Chaining
{
  partial class UTXOTable
  {
    public class BlockArchive
    {
      public byte[] Buffer;
      public int IndexBuffer;

      public int Index;
      public bool IsInvalid;
      public bool IsLastArchive;

      public Header HeaderTip;
      public Header HeaderRoot;
      public double Difficulty;
      public int Height;

      public int CountTX;

      public List<TXInput> Inputs = new List<TXInput>();
      public UTXOIndexUInt32 TableUInt32 = new UTXOIndexUInt32();
      public UTXOIndexULong64 TableULong64 = new UTXOIndexULong64();
      public UTXOIndexUInt32Array TableUInt32Array = 
        new UTXOIndexUInt32Array();
          
      SHA256 SHA256 = SHA256.Create();

      public Stopwatch StopwatchInsertion = new Stopwatch();
      public Stopwatch StopwatchParse = new Stopwatch();


      public void Reset()
      {
        HeaderTip = null;
        Height = 0;
        Difficulty = 0.0;
        CountTX = 0;
      }

           

      public void Parse(byte[] buffer)
      {
        Parse(buffer, buffer.Length);
      }
      public void Parse(byte[] buffer, byte[] hashStopLoading)
      {
        Parse(buffer, buffer.Length, hashStopLoading);
      }

      readonly byte[] HASH_ZERO = new byte[32];
      public void Parse(byte[] buffer, int countBytes)
      {
        Parse(buffer, countBytes, HASH_ZERO);
      }

      public void Parse(
        byte[] buffer, 
        int countBytes,
        byte[] hashStopLoading)
      {
        Buffer = buffer;
        IndexBuffer = 0;

        if (VarInt.GetInt32(Buffer, ref IndexBuffer) == 0)
        {
          return;
        }

        StopwatchParse.Start();

        do
        {
          ParseBlock();
        } while (
          !hashStopLoading.IsEqual(HeaderTip.Hash) &&
          IndexBuffer < countBytes);
        
        StopwatchParse.Stop();
      }


      
      public void ParseBlock(byte[] buffer)
      {
        Buffer = buffer;
        IndexBuffer = 0;

        ParseBlock();
      }

      void ParseBlock()
      {
        Header header = Header.ParseHeader(
          Buffer,
          ref IndexBuffer,
          SHA256);

        if(HeaderTip == null)
        {
          HeaderTip = header;
          HeaderRoot = header;
        }
        else if (!HeaderTip.Hash.IsEqual(
          header.HashPrevious))
        {
          throw new ChainException(
            string.Format(
              "headerchain out of order in blockArchive {0}",
              Index));
        }
        else
        {
          header.HeaderPrevious = HeaderTip;
          HeaderTip.HeaderNext = header;
          HeaderTip = header;
        }

        Difficulty += header.Difficulty;
        Height += 1;

        int tXCount = VarInt.GetInt32(Buffer, ref IndexBuffer);
        CountTX += tXCount;

        if (tXCount == 0)
        {
          return;
        }

        if (tXCount == 1)
        {
          byte[] tXHash = ParseTX(true);

          if (!tXHash.IsEqual(header.MerkleRoot))
          {
            throw new ChainException(
              "Payload merkle root corrupted");
          }

          return;
        }

        int tXsLengthMod2 = tXCount & 1;
        var merkleList = new byte[tXCount + tXsLengthMod2][];

        merkleList[0] = ParseTX(true);

        for (int t = 1; t < tXCount; t += 1)
        {
          merkleList[t] = ParseTX(false);
        }

        if (tXsLengthMod2 != 0)
        {
          merkleList[tXCount] = merkleList[tXCount - 1];
        }

        if (!GetRoot(merkleList).IsEqual(header.MerkleRoot))
        {
          throw new ChainException(
            "Payload hash unequal with merkle root.");
        }
        
        return;
      }

      byte[] ParseTX(bool isCoinbase)
      {
        try
        {
          int tXStartIndex = IndexBuffer;

          IndexBuffer += 4; // BYTE_LENGTH_VERSION

          bool isWitnessFlagPresent = Buffer[IndexBuffer] == 0x00;
          if (isWitnessFlagPresent)
          {
            throw new NotImplementedException("Parsing of segwit txs not implemented");
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
              TXInput input = new TXInput(Buffer, ref IndexBuffer);

              if (
                !TableUInt32.TrySpend(input) &&
                !TableULong64.TrySpend(input) &&
                !TableUInt32Array.TrySpend(input))
              {
                Inputs.Add(input);
              }
            }
          }

          int countTXOutputs = VarInt.GetInt32(Buffer, ref IndexBuffer);

          for (int i = 0; i < countTXOutputs; i += 1)
          {
            IndexBuffer += 8; // BYTE_LENGTH_OUTPUT_VALUE
            int lengthLockingScript = VarInt.GetInt32(Buffer, ref IndexBuffer);
            IndexBuffer += lengthLockingScript;
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
          
          byte[] tXHash = SHA256.ComputeHash(
           SHA256.ComputeHash(
             Buffer,
             tXStartIndex,
             IndexBuffer - tXStartIndex));
                   
          int lengthUTXOBits = COUNT_NON_OUTPUT_BITS + countTXOutputs;

          if (LENGTH_BITS_UINT >= lengthUTXOBits)
          {
            TableUInt32.ParseUTXO(
              lengthUTXOBits,
              tXHash);
          }
          else if (LENGTH_BITS_ULONG >= lengthUTXOBits)
          {
            TableULong64.ParseUTXO(
              lengthUTXOBits,
              tXHash);
          }
          else
          {
            TableUInt32Array.ParseUTXO(
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
        byte[] leafPair = new byte[2 * HASH_BYTE_SIZE];

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
