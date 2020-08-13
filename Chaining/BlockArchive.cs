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
      public List<TXInput> Inputs = new List<TXInput>();

      UTXOIndexUInt32 TableUInt32 = new UTXOIndexUInt32();
      UTXOIndexULong64 TableULong64 = new UTXOIndexULong64();
      UTXOIndexUInt32Array TableUInt32Array = new UTXOIndexUInt32Array();
      public KeyValuePair<byte[], uint>[] UTXOsUInt32;
      public KeyValuePair<byte[], ulong>[] UTXOsULong64;
      public KeyValuePair<byte[], uint[]>[] UTXOsUInt32Array;

      public Header HeaderTip;
      public Header HeaderRoot;
      public double Difficulty;
      public int Height;

      public int CountBlock;
      public int CountTX;
      SHA256 SHA256 = SHA256.Create();

      const int SIZE_MESSAGE_PAYLOAD_BUFFER = 0x2000000;
      public byte[] Buffer = new byte[SIZE_MESSAGE_PAYLOAD_BUFFER];
      public int IndexBuffer;

      public bool IsInvalid;
      public bool IsCancellationBatch;

      public Stopwatch StopwatchStaging = new Stopwatch();
      public Stopwatch StopwatchParse = new Stopwatch();

      public int Index;

      public BlockArchive()
      { }

      public BlockArchive(byte[] buffer)
      {
        buffer.CopyTo(Buffer, 0);
      }

      public BlockArchive(int archiveIndex)
      {
        Index = archiveIndex;
      }

      public BlockArchive(
        int archiveIndex,
        byte[] buffer)
      {
        buffer.CopyTo(Buffer, 0);
        Index = archiveIndex;
      }


      public BlockArchive(Header header)
      {
        HeaderTip = header;
      }



      const int COUNT_HEADER_BYTES = 80;

      const int BYTE_LENGTH_VERSION = 4;
      const int BYTE_LENGTH_OUTPUT_VALUE = 8;
      const int BYTE_LENGTH_LOCK_TIME = 4;
      const int OFFSET_INDEX_MERKLE_ROOT = 36;
      const int TWICE_HASH_BYTE_SIZE = HASH_BYTE_SIZE << 1;
      readonly byte[] HASH_ZERO = new byte[32];
      
      int TXCount;

      public void Parse()
      {
        Parse(HASH_ZERO);
      }
      public void Parse(byte[] hashStopLoading)
      {
        StopwatchParse.Start();

        IndexBuffer = 0;

        int countHeaders = VarInt.GetInt32(Buffer, ref IndexBuffer);

        if(countHeaders == 0)
        {
          HeaderRoot = null;
          return;
        }

        Header header = Header.ParseHeader(
          Buffer,
          ref IndexBuffer,
          SHA256);

        ParseBlock(header.MerkleRoot);

        HeaderRoot = header;
        HeaderTip = header;
               
        while (
          !HeaderTip.Hash.IsEqual(hashStopLoading) && 
          IndexBuffer < Buffer.Length)
        {
          header = Header.ParseHeader(
            Buffer,
            ref IndexBuffer,
            SHA256);

          if (!header.HashPrevious.IsEqual(HeaderTip.Hash))
          {
            throw new ChainException(
              string.Format(
                "headerchain out of order in blockArchive {0}",
                Index));
          }

          header.HeaderPrevious = HeaderTip;
          HeaderTip.HeaderNext = header;
          HeaderTip = header;

          ParseBlock(header.MerkleRoot);
        }

        ConvertTablesToArrays();

        StopwatchParse.Stop();
      }

      public void Parse(int startIndex, byte[] merkleRoot)
      {
        IndexBuffer = 0;

        Header.ParseHeader(
          Buffer,
          ref IndexBuffer,
          SHA256);

        ParseBlock(merkleRoot);

        ConvertTablesToArrays();
      }


      void ParseBlock(byte[] merkleRoot)
      {
        TXCount = VarInt.GetInt32(Buffer, ref IndexBuffer);

        if(TXCount == 0)
        {
          return;
        }

        if (TXCount == 1)
        {
          byte[] tXHash = ParseTX(true);

          if (!tXHash.IsEqual(merkleRoot))
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

        if (!GetRoot(merkleList).IsEqual(merkleRoot))
        {
          throw new ChainException("Payload hash unequal with merkle root.");
        }

        CountBlock += 1;
        CountTX += TXCount;

        return;
      }

      byte[] ParseTX(bool isCoinbase)
      {
        try
        {
          int tXStartIndex = IndexBuffer;

          IndexBuffer += BYTE_LENGTH_VERSION;

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

              AddInput(input);
            }
          }

          int countTXOutputs = VarInt.GetInt32(Buffer, ref IndexBuffer);

          for (int i = 0; i < countTXOutputs; i += 1)
          {
            IndexBuffer += BYTE_LENGTH_OUTPUT_VALUE;
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

          IndexBuffer += BYTE_LENGTH_LOCK_TIME;

          int tXLength = IndexBuffer - tXStartIndex;

          byte[] tXHash = SHA256.ComputeHash(
           SHA256.ComputeHash(
             Buffer,
             tXStartIndex,
             tXLength));

          AddOutput(
            tXHash,
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

                    

      void ConvertTablesToArrays()
      {
        UTXOsUInt32 = TableUInt32.Table.ToArray();
        UTXOsULong64 = TableULong64.Table.ToArray();
        UTXOsUInt32Array = TableUInt32Array.Table.ToArray();
      }



      void AddInput(TXInput input)
      {
        if (
          !TableUInt32.TrySpend(input) &&
          !TableULong64.TrySpend(input) &&
          !TableUInt32Array.TrySpend(input))
        {
          Inputs.Add(input);
        }
      }



      void AddOutput(
        byte[] tXHash,
        int countTXOutputs)
      {
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
      }


      public void IncrementHeaderRoot()
      {
        Difficulty -= HeaderRoot.Difficulty;
        Height -= 1;
        HeaderRoot = HeaderRoot.HeaderNext;
      }
    }
  }
}
