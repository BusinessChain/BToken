using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace BToken.Accounting
{
  public partial class UTXO
  {
    class Block
    {
      public byte[] Buffer;
      public byte[] HeaderHash;
      public string HeaderHashString;

      public int TXCount;
      public UTXOItem[][] UTXOItemsPerTable;
      public int[] IndexesUTXOItems;
      public TXInput[][] InputsPerTX;

      public Block(int tXCount, byte[] headerHash)
      {
        HeaderHash = headerHash;
        HeaderHashString = headerHash.ToHexString();

        TXCount = tXCount;

        UTXOItemsPerTable = new UTXOItem[][]{
          new UTXOItemUInt32[tXCount],
          new UTXOItemULong64[tXCount],
          new UTXOItemByteArray[tXCount]};

        IndexesUTXOItems = new int[UTXOItemsPerTable.Length];

        InputsPerTX = new TXInput[tXCount][];
      }

      public void PushUTXOItem(int address, UTXOItem uTXOItem)
      {
        UTXOItemsPerTable[address][IndexesUTXOItems[address]] = uTXOItem;
        IndexesUTXOItems[address] += 1;
      }
      public bool TryPopUTXOItem(int address, out UTXOItem uTXOItem)
      {
        IndexesUTXOItems[address] -= 1;

        if (IndexesUTXOItems[address] < 0)
        {
          uTXOItem = null;
          return false;
        }

        uTXOItem = UTXOItemsPerTable[address][IndexesUTXOItems[address]];
        return true;
      }

    }
  }
}
