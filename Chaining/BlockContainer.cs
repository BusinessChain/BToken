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
    public class BlockContainer : DataContainer
    {
      public List<TXInput> Inputs = new List<TXInput>();

      UTXOIndexUInt32 TableUInt32 = new UTXOIndexUInt32();
      UTXOIndexULong64 TableULong64 = new UTXOIndexULong64();
      UTXOIndexUInt32Array TableUInt32Array = new UTXOIndexUInt32Array();
      public KeyValuePair<byte[], uint>[] UTXOsUInt32;
      public KeyValuePair<byte[], ulong>[] UTXOsULong64;
      public KeyValuePair<byte[], uint[]>[] UTXOsUInt32Array;

      public Header HeaderPrevious;
      public Header Header;

      public List<Header> Headers = new List<Header>();
      public List<int> BufferStartIndexesBlocks = new List<int>();

      public int BlockCount;
      SHA256 SHA256 = SHA256.Create();

      public Stopwatch StopwatchMerging = new Stopwatch();
      public Stopwatch StopwatchParse = new Stopwatch();



      public BlockContainer(
        Headerchain headerchain,
        int archiveIndex)
        : base(archiveIndex)
      {
        Headerchain = headerchain;
      }

      public BlockContainer(
        Headerchain headerchain,
        int archiveIndex,
        byte[] blockBytes)
        : base(
            archiveIndex,
            blockBytes)
      {
        Headerchain = headerchain;
      }


      public BlockContainer(
        Headerchain headerchain,
        Header header)
      {
        Headerchain = headerchain;
        Header = header;
      }



      const int COUNT_HEADER_BYTES = 80;

      const int BYTE_LENGTH_VERSION = 4;
      const int BYTE_LENGTH_OUTPUT_VALUE = 8;
      const int BYTE_LENGTH_LOCK_TIME = 4;
      const int OFFSET_INDEX_MERKLE_ROOT = 36;
      const int TWICE_HASH_BYTE_SIZE = HASH_BYTE_SIZE << 1;

      int BufferIndex;
      byte[] HeaderHash;
      int TXCount;
      
      Headerchain Headerchain;

      public override bool TryParse()
      {
        StopwatchParse.Start();

        try
        {
          BufferIndex = 0;

          BufferStartIndexesBlocks.Add(BufferIndex);

          HeaderHash =
            SHA256.ComputeHash(
              SHA256.ComputeHash(
                Buffer,
                BufferIndex,
                COUNT_HEADER_BYTES));

          BufferIndex += COUNT_HEADER_BYTES;
          TXCount = VarInt.GetInt32(Buffer, ref BufferIndex);

          if (Header == null)
          {
            Header = Headerchain.ReadHeader(HeaderHash, SHA256);
          }
          else
          {
            ValidateHeaderHash(
              HeaderHash,
              Header.HeaderHash);
          }

          Headers.Add(Header);

          HeaderPrevious = Header.HeaderPrevious;
          
          ParseBlock(OFFSET_INDEX_MERKLE_ROOT);
          BlockCount += 1;
          CountItems += TXCount;

          while (BufferIndex < Buffer.Length)
          {
            BufferStartIndexesBlocks.Add(BufferIndex);

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

            Headers.Add(Header);

            ParseBlock(merkleRootIndex);
            BlockCount += 1;
            CountItems += TXCount;
          }

          ConvertTablesToArrays();
        }
        catch (Exception ex)
        {
          IsValid = false;

          Console.WriteLine(
            "Exception {0} loading archive {1}: {2}",
            ex.GetType().Name,
            Index,
            ex.Message);

          return false;
        }

        StopwatchParse.Stop();

        return true;
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

              AddInput(input);
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

                    

      public void ConvertTablesToArrays()
      {
        UTXOsUInt32 = TableUInt32.Table.ToArray();
        UTXOsULong64 = TableULong64.Table.ToArray();
        UTXOsUInt32Array = TableUInt32Array.Table.ToArray();
      }



      public void AddInput(TXInput input)
      {
        if (
          !TableUInt32.TrySpend(input) &&
          !TableULong64.TrySpend(input) &&
          !TableUInt32Array.TrySpend(input))
        {
          Inputs.Add(input);
        }
      }



      public void AddOutput(
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
