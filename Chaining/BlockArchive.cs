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
    public class BlockArchive : DataContainer
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

      public int BlockCount;
      public int CountTX;
      SHA256 SHA256;

      public byte[] Buffer;
      public int IndexBuffer;

      public Stopwatch StopwatchStaging = new Stopwatch();
      public Stopwatch StopwatchParse = new Stopwatch();



      public BlockArchive()
      { }

      public BlockArchive(byte[] buffer)
        : base(buffer)
      { }

      public BlockArchive(
        int archiveIndex)
        : base(archiveIndex)
      { }

      public BlockArchive(
        int archiveIndex,
        byte[] blockBytes)
        : base(
            archiveIndex,
            blockBytes)
      { }


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
      
      int TXCount;

      public override void Parse(SHA256 sHA256)
      {
        SHA256 = sHA256;

        StopwatchParse.Start();

        IndexBuffer = 0;
        
        HeaderRoot = Header.ParseHeader(
          Buffer,
          ref IndexBuffer,
          sHA256);
        TXCount = VarInt.GetInt32(Buffer, ref IndexBuffer);

        HeaderTip = HeaderRoot;

        ParseBlock(OFFSET_INDEX_MERKLE_ROOT);      

        while (IndexBuffer < Buffer.Length)
        {
          int merkleRootIndex = IndexBuffer + OFFSET_INDEX_MERKLE_ROOT;

          var header = Header.ParseHeader(
            Buffer,
            ref IndexBuffer,
            sHA256);
          TXCount = VarInt.GetInt32(Buffer, ref IndexBuffer);
          
          if (!header.HashPrevious.IsEqual(HeaderTip.Hash))
          {
            throw new ChainException(
              string.Format(
                "header {0} with header hash previous {1} " +
                "not in consecutive order with current tip header {2}",
                header.Hash.ToHexString(),
                header.HashPrevious.ToHexString(),
                HeaderTip.Hash.ToHexString()));
          }

          header.HeaderPrevious = HeaderTip;
          HeaderTip = header;

          ParseBlock(merkleRootIndex);
        }

        ConvertTablesToArrays();

        StopwatchParse.Stop();
      }

      public void ValidateHeaderHash(byte[] hashValidator)
      {
        if (!HeaderTip.Hash.IsEqual(hashValidator))
        {
          throw new ChainException(
            string.Format("Unexpected header hash {0}, \nexpected {1}",
            HeaderTip.Hash.ToHexString(),
            hashValidator.ToHexString()));
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
          throw new ChainException("Payload hash unequal with merkle root.");
        }

        BlockCount += 1;
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
    }
  }
}
