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
      public int BatchIndex;
      public byte[] Buffer;
      public int BufferIndex;
      public List<Block> Blocks = new List<Block>(200);
      public int[] IndexesUTXOItems = new int[3];

      public int BlockIndex = -1;
      public int InputIndex;
      public int TXIndex;

      public Headerchain.ChainHeader ChainHeader;
      public SHA256 SHA256Generator = SHA256.Create();

      public readonly TaskCompletionSource<UTXOBatch> SignalBatchCompletion =
        new TaskCompletionSource<UTXOBatch>();

      public Stopwatch StopwatchMerging = new Stopwatch();
      public Stopwatch StopwatchResolver = new Stopwatch();
      public Stopwatch StopwatchParse = new Stopwatch();


      public UTXOBatch()
      { }
      public UTXOBatch(int batchindex)
      { }
      
      public void PushBlock(Block block)
      {
        Blocks.Add(block);

        BlockIndex += 1;

        TXIndex = 0;
        InputIndex = 0;

        for(int i = 0; i < IndexesUTXOItems.Length; i += 1)
        {
          IndexesUTXOItems[i] = 0;
        }
      }
      public void InitializeInputs(int countInputs, int tXIndex)
      {
        Blocks[BlockIndex].InputsPerTX[tXIndex] = new TXInput[countInputs];
        InputIndex = 0;
      }
      public void PushTXInput(int tXIndex, TXInput tXInput)
      {
        Blocks[BlockIndex]
          .InputsPerTX[tXIndex][InputIndex] = tXInput;

        InputIndex += 1;
      }
      public void PushUTXOItem(int address, UTXOItem uTXOItem)
      {
        Blocks[BlockIndex]
          .UTXOItemsPerTable[address][IndexesUTXOItems[address]] = uTXOItem;

        IndexesUTXOItems[address] += 1;
      }

      public bool TryPopInput(out TXInput input)
      {
        if(BlockIndex < 0)
        {
          input = null;
          return false;
        }

        input = Blocks[BlockIndex].InputsPerTX[TXIndex][InputIndex];

        InputIndex -= 1;
        if(InputIndex < 0)
        {
          TXIndex -= 1;

          if(TXIndex < 0)
          {
            Blocks.RemoveAt(BlockIndex);
            BlockIndex -= 1;
            if(BlockIndex < 0)
            {
              return true;
            }

            TXIndex = Blocks[BlockIndex].TXCount - 1;
          }

          InputIndex = Blocks[BlockIndex].InputsPerTX[TXIndex].Length - 1;
        }
        
        return true;
      }
      public bool TryPopUTXOItem(int address, out UTXOItem uTXODataItem)
      {
        IndexesUTXOItems[address] -= 1;

        if (IndexesUTXOItems[address] < 0)
        {
          uTXODataItem = null;
          return false;
        }

        uTXODataItem = Blocks[BlockIndex]
          .UTXOItemsPerTable[address][IndexesUTXOItems[address]];
        
        return true;
      }
    }
  }
}
