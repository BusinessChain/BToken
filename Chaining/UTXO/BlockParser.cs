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
      public int Index;
      public bool IsInvalid;

      public byte[] Buffer;
      public int IndexBuffer;

      public bool IsBufferOverflow;

      public Header HeaderTipOverflow;
      public Header HeaderRootOverflow;
      public Header HeaderTip;
      public Header HeaderRoot;
      public double Difficulty;
      public int Height;

      public int CountTX;
          
      SHA256 SHA256 = SHA256.Create();

      public Stopwatch StopwatchInsertion = new Stopwatch();
      public Stopwatch StopwatchParse = new Stopwatch();
                 

      readonly byte[] HASH_ZERO = new byte[32];
      public void Parse(byte[] buffer)
      {
        Parse(
          buffer, 
          buffer.Length,
          0,
          HASH_ZERO);
      }

      public void ParseHeaders(
        byte[] buffer,
        int countBytes)
      {
        int indexPayload = 0;

        int countHeaders = VarInt.GetInt32(
          buffer,
          ref indexPayload);

        if(countHeaders == 0)
        {
          Height = 0;
          HeaderRoot = null;
          return;
        }

        Parse(
          buffer,
          countBytes,
          indexPayload,
          HASH_ZERO);
      }

      public void Parse(
        byte[] buffer, 
        byte[] hashStopLoading)
      {
        Parse(
          buffer, 
          buffer.Length,
          0,
          hashStopLoading);
      }

      void Parse(
        byte[] buffer, 
        int countBytes,
        int offset,
        byte[] hashStopLoading)
      {
        StopwatchParse.Restart();

        Buffer = buffer;
        IndexBuffer = offset;
                
        Header header = Header.ParseHeader(
          Buffer,
          ref IndexBuffer,
          SHA256);

        HeaderRoot = header;
        HeaderTip = header;
        Height = 1;
        Difficulty = header.Difficulty;

        ParseTXs(header.MerkleRoot);

        while (
          IndexBuffer < countBytes &&
          !hashStopLoading.IsEqual(HeaderTip.Hash))
        {
          header = Header.ParseHeader(
           Buffer,
           ref IndexBuffer,
           SHA256);

          if (!HeaderTip.Hash.IsEqual(header.HashPrevious))
          {
            throw new ChainException(
              string.Format(
                "headerchain out of order in blockArchive {0}",
                Index));
          }

          header.HeaderPrevious = HeaderTip;
          HeaderTip.HeaderNext = header;
          HeaderTip = header;

          Height += 1;
          Difficulty += header.Difficulty;

          ParseTXs(header.MerkleRoot);
        }

        StopwatchParse.Stop();
      }



      public void ClearPayloadData()
      {
        IndexBuffer = 0;

        Header = null;

        CountTX = 0;
      }


      Header Header;

      public Block ParseBlock(
        byte[] buffer,
        int startIndex)
      {
        StopwatchParse.Start();

        Buffer = buffer;
        IndexBuffer = startIndex;

        Header = Header.ParseHeader(
          Buffer,
          ref IndexBuffer,
          SHA256);

        Console.WriteLine(
          "Parse block {0}",
          Header.Hash.ToHexString());

        List<TX> tXs = ParseTXs(Header.MerkleRoot);
        
        StopwatchParse.Stop();

        return new Block(
          Buffer,
          Header,
          tXs);
      }

      public void RecoverFromOverflow()
      {
        IsBufferOverflow = false;

        HeaderRoot = HeaderRootOverflow;
        HeaderRootOverflow = null;

        HeaderTip = HeaderTipOverflow;
        HeaderTipOverflow = null;

        CalculateHeightAndDifficulty();
      }

      void CalculateHeightAndDifficulty()
      {
        var header = HeaderRoot;

        Height = 1;
        Difficulty = HeaderRoot.Difficulty;

        while (header != HeaderTip)
        {
          header = header.HeaderNext;

          Height += 1;
          Difficulty += header.Difficulty;
        }
      }

      List<TX> ParseTXs(byte[] hashMerkleRoot)
      {
        List<TX> tXs = new List<TX>();

        int tXCount = VarInt.GetInt32(
          Buffer,
          ref IndexBuffer);

        if (tXCount == 0)
        { }
        else if (tXCount == 1)
        {
          TX tX = ParseTX(true);
          tXs.Add(tX);

          if (!tX.Hash.IsEqual(hashMerkleRoot))
          {
            throw new ChainException(
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
            throw new ChainException(
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
          
          int lengthUTXOBits = 
            COUNT_NON_OUTPUT_BITS + countTXOutputs;
          
          return tX;
        }
        catch (ArgumentOutOfRangeException)
        {
          throw new ChainException(
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
