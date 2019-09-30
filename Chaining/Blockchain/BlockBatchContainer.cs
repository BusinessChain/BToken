using System;
using System.Collections.Generic;
using System.Linq;
using System.Diagnostics;
using System.Threading.Tasks;
using System.Security.Cryptography;

namespace BToken.Chaining
{
  public partial class Blockchain
  {
    partial class UTXOTable : IDatabase
    {
      public class BlockBatchContainer : ItemBatchContainer
      {
        const int AVERAGE_INPUTS_PER_TX = 5;
        public List<TXInput> Inputs = new List<TXInput>(COUNT_TXS_IN_BATCH_FILE * AVERAGE_INPUTS_PER_TX);

        public UTXOIndexUInt32 TableUInt32 = new UTXOIndexUInt32();
        public KeyValuePair<byte[], uint>[] UTXOsUInt32;
        public UTXOIndexULong64 TableULong64 = new UTXOIndexULong64();
        public KeyValuePair<byte[], ulong>[] UTXOsULong64;
        public UTXOIndexUInt32Array TableUInt32Array = new UTXOIndexUInt32Array();
        public KeyValuePair<byte[], uint[]>[] UTXOsUInt32Array;

        public Header HeaderPrevious;
        public Header HeaderRoot;
        public Header HeaderLast;

        public int BlockCount;

        BlockParser BlockParser;


        public BlockBatchContainer(
          BlockParser blockParser,
          int archiveIndex,
          byte[] blockBytes)
          : base(
              archiveIndex,
              blockBytes)
        {
          BlockParser = blockParser;
        }


        public BlockBatchContainer(
          BlockParser blockParser,
          Header header)
          : base(null)
        {
          BlockParser = blockParser;
          HeaderRoot = header;
          HeaderLast = header;
        }



        public override void Parse()
        {
          StopwatchParse.Start();

          BlockParser.Parse(this);

          StopwatchParse.Stop();
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
          int batchIndex,
          int countTXOutputs)
        {
          int lengthUTXOBits = CountNonOutputBits + countTXOutputs;

          if (LENGTH_BITS_UINT >= lengthUTXOBits)
          {
            TableUInt32.ParseUTXO(
              batchIndex,
              lengthUTXOBits,
              tXHash);
          }
          else if (LENGTH_BITS_ULONG >= lengthUTXOBits)
          {
            TableULong64.ParseUTXO(
              batchIndex,
              lengthUTXOBits,
              tXHash);
          }
          else
          {
            TableUInt32Array.ParseUTXO(
              batchIndex,
              lengthUTXOBits,
              tXHash);
          }
        }
      }
    }
  }
}
