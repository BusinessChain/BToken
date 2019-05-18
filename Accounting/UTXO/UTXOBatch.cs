using System;
using System.Diagnostics;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Security.Cryptography;

using BToken.Chaining;

namespace BToken.Accounting
{
  public partial class UTXO
  {
    class UTXOBatch
    {
      public int Index;
      public byte[] Buffer;
      public TXInput[][] Inputs;
      public UTXODataItem[][] UTXODataItems;
      public int[] IndexesUTXODataBatchs;

      public Headerchain.ChainHeader ChainHeader;
      public SHA256 SHA256Generator = SHA256.Create();

      public readonly TaskCompletionSource<UTXOBatch> SignalBatchCompletion =
        new TaskCompletionSource<UTXOBatch>();

      public Stopwatch StopwatchMerging = new Stopwatch();
      public Stopwatch StopwatchParse = new Stopwatch();


      public UTXOBatch()
      { }
      public UTXOBatch(int batchindex)
      { }

      public void InitializeDataBatches(int tXCount)
      {
        UTXODataItems = new UTXODataItem[][]{
        new UTXOIndexUInt32DataItem[tXCount],
        new UTXOIndexULong64DataBatch[tXCount],
        new UTXOIndexByteArrayDataBatch[tXCount]};

        Inputs = new TXInput[tXCount][];
      }

      public void PushUTXODataItem(int address, UTXODataItem uTXOIndexDataBatch)
      {
        UTXODataItems[address][IndexesUTXODataBatchs[address]] = uTXOIndexDataBatch;
        IndexesUTXODataBatchs[address] += 1;
      }

      public bool TryGetDataItem(int address, out UTXODataItem uTXODataItem)
      {
        if(IndexesUTXODataBatchs[address] < 0)
        {
          uTXODataItem = null;
          return false;
        }

        uTXODataItem = UTXODataItems[address][IndexesUTXODataBatchs[address]];
        IndexesUTXODataBatchs[address] -= 1;

        return true;
      }
    }
  }
}
