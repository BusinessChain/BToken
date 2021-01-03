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

      public byte[] ArchiveBuffer = new byte[0x2000000];
      public int IndexArchiveBuffer;
      public bool IsArchiveBufferOverflow;
      public byte[] Buffer;
      public int IndexBuffer;

      public Header HeaderTipOverflow;
      public Header HeaderRootOverflow;
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

      public Wallet Wallet = new Wallet();
          
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
        IndexArchiveBuffer = 0;

        Header = null;

        CountTX = 0;
        Inputs.Clear();
        TableUInt32.Table.Clear();
        TableULong64.Table.Clear();
        TableUInt32Array.Table.Clear();

        Wallet.Clear();
      }

      public void SetupBlockDownload(
        int index,
        ref Header headerLoad,
        int countMax)
      {
        Index = index;

        HeaderRoot = headerLoad;
        Height = 0;
        Difficulty = 0.0;

        do
        {
          HeaderTip = headerLoad;
          Height += 1;
          Difficulty += headerLoad.Difficulty;

          headerLoad = headerLoad.HeaderNext;
        } while (
        Height < countMax
        && headerLoad != null);
      }


      public bool AreAllBlockReceived;
      Header Header;

      public void ParsePayload(
        byte[] buffer, 
        int bufferLength)
      {
        byte[] hash =
          SHA256.ComputeHash(
            SHA256.ComputeHash(
              buffer,
              0,
              Header.COUNT_HEADER_BYTES));

        Console.WriteLine(
          "parse block {0}", 
          hash.ToHexString());

        if(Header == null)
        {
          Header = HeaderRoot;
        }

        if (!hash.IsEqual(Header.Hash))
        {
          throw new ChainException(string.Format(
            "Unexpected block header {0} in blockParser {1}. \n" +
            "Excpected {2}.",
            hash.ToHexString(),
            Index,
            Header.Hash.ToHexString()));
        }
        
        try
        {
          Array.Copy(
            buffer,
            0,
            ArchiveBuffer,
            IndexArchiveBuffer,
            bufferLength);

          IsArchiveBufferOverflow = false;
        }
        catch (ArgumentException)
        {
          Console.WriteLine(
            "Overflow archive buffer in blockParser {0}.",
            Index);

          IsArchiveBufferOverflow = true;

          HeaderRootOverflow = Header;
          HeaderTipOverflow = HeaderTip;
          HeaderTip = Header.HeaderPrevious;

          AreAllBlockReceived = true;

          CalculateHeightAndDifficulty();

          return;
        }

        Buffer = ArchiveBuffer;
        IndexBuffer = IndexArchiveBuffer +
          Header.COUNT_HEADER_BYTES;

        IndexArchiveBuffer += bufferLength;

        StopwatchParse.Start();

        ParseTXs(Header.MerkleRoot);

        AreAllBlockReceived = Header == HeaderTip;
        Header = Header.HeaderNext;

        StopwatchParse.Stop();
      }

      public void RecoverFromOverflow()
      {
        IsArchiveBufferOverflow = false;

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

      void ParseTXs(
        byte[] merkleRootHeader)
      {
        int tXCount = VarInt.GetInt32(
          Buffer,
          ref IndexBuffer);

        if(tXCount == 0)
        {
          return;
        }

        if (tXCount == 1)
        {
          byte[] tXHash = ParseTX(true);

          if (!tXHash.IsEqual(merkleRootHeader))
          {
            throw new ChainException(
              "Payload merkle root corrupted");
          }
        }
        else
        {
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

          if (!GetRoot(merkleList).IsEqual(merkleRootHeader))
          {
            throw new ChainException(
              "Payload hash unequal with merkle root.");
          }
        }

        CountTX += tXCount;
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

          int countTXOutputs = VarInt.GetInt32(
            Buffer, 
            ref IndexBuffer);

          Wallet.DetectTXOutputsSpendable(
            countTXOutputs, 
            Buffer,
            ref IndexBuffer);

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
          
          Wallet.CreateTXInputsSignable(tXHash);
          
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
