using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.IO;
using System.Linq;

namespace BToken.Accounting
{
  public partial class UTXO
  {
    abstract class UTXOIndex
    {
      public int Address;
      public int OffsetCollisionBits;
      
      public int PrimaryKey;


      protected UTXOIndex(
        int address,
        string label)
      {
        Address = address;
        Label = label;

        OffsetCollisionBits = 
          COUNT_BATCHINDEX_BITS 
          + COUNT_HEADER_BITS 
          + address * COUNT_COLLISION_BITS_PER_TABLE;

        DirectoryPath = Path.Combine(PathUTXOState, Label);
      }
      
      protected abstract void SpendUTXO(byte[] key, int outputIndex, out bool areAllOutputpsSpent);
      protected abstract bool TryGetValueInTable(byte[] key);
      protected abstract void RemoveCollision(byte[] key);
      
    }
  }
}
