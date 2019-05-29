using System;
using System.Collections.Generic;
using System.Linq;

namespace BToken.Accounting
{
  public partial class UTXO
  {
    class UTXOTableUInt32 : UTXOTable
    {
      Dictionary<int, uint> PrimaryTable = new Dictionary<int, uint>();
      Dictionary<byte[], uint> CollisionTable =
        new Dictionary<byte[], uint>(new EqualityComparerByteArray());

      uint UTXOPrimary;
      uint UTXOCollision;

      const int COUNT_INTEGER_BITS = 32;

      static readonly uint MaskAllOutputBitsSpent = uint.MaxValue << CountNonOutputBits;
      static readonly uint MaskBatchIndex = ~(uint.MaxValue << COUNT_BATCHINDEX_BITS);
      static readonly uint MaskHeaderBits =
        ~((uint.MaxValue << (COUNT_BATCHINDEX_BITS + COUNT_HEADER_BITS)) | MaskBatchIndex);

      public readonly static uint[] MasksCollisionBitsClear = {
        0xFFCFFFFF,
        0xFF3FFFFF,
        0xFCFFFFFF };
      public readonly static uint[] MasksCollisionBitsOne = {
        0x00100000,
        0x00400000,
        0x01000000 };
      public readonly static uint[] MasksCollisionBitsTwo = {
        0x00200000,
        0x00800000,
        0x02000000 };
      public readonly static uint[] MasksCollisionBitsFull = {
        0x00300000,
        0x00C00000,
        0x03000000 };

      public UTXOTableUInt32()
        : base(0, "UInt32")
      { }

      protected override int GetCountPrimaryTableItems()
      {
        return PrimaryTable.Count;
      }
      protected override int GetCountSecondaryTableItems()
      {
        return CollisionTable.Count;
      }
      public override bool PrimaryTableContainsKey(int primaryKey)
      {
        return PrimaryTable.ContainsKey(primaryKey);
      }
      public override void IncrementCollisionBits(int primaryKey, int collisionAddress)
      {
        uint collisionBits = PrimaryTable[primaryKey] & MasksCollisionBitsFull[collisionAddress];
        if (collisionBits == 0)
        {
          PrimaryTable[primaryKey] |= MasksCollisionBitsOne[collisionAddress];
          return;
        }

        if (collisionBits == MasksCollisionBitsOne[collisionAddress])
        {
          PrimaryTable[primaryKey] &= MasksCollisionBitsClear[collisionAddress];
          PrimaryTable[primaryKey] |= MasksCollisionBitsTwo[collisionAddress];
          return;
        }

        if (collisionBits == MasksCollisionBitsTwo[collisionAddress])
        {
          PrimaryTable[primaryKey] |= MasksCollisionBitsFull[collisionAddress];
        }
      }
      public override void SecondaryTableAddUTXO(UTXOItem uTXOItem)
      {
        CollisionTable.Add(
          uTXOItem.Hash, 
          ((UTXOItemUInt32)uTXOItem).UTXOIndex);
      }
      public override void PrimaryTableAddUTXO(UTXOItem uTXODataItem)
      {
        PrimaryTable.Add(
          uTXODataItem.PrimaryKey, 
          ((UTXOItemUInt32)uTXODataItem).UTXOIndex);
      }

      public override void SpendPrimaryUTXO(TXInput input, out bool areAllOutputpsSpent)
      {
        SpendUTXO(ref UTXOPrimary, input.OutputIndex, out areAllOutputpsSpent);
        PrimaryTable[PrimaryKey] = UTXOPrimary;
      }
      public override bool TryGetValueInPrimaryTable(int primaryKey)
      {
        PrimaryKey = primaryKey; // cache
        return PrimaryTable.TryGetValue(primaryKey, out UTXOPrimary);
      }
      public override bool HasCollision(int cacheAddress)
      {
        return (MasksCollisionBitsFull[cacheAddress] & UTXOPrimary) != 0;
      }
      public override void RemovePrimary()
      {
        PrimaryTable.Remove(PrimaryKey);
      }
      public override uint GetCollisionBits()
      {
        return MaskCollisionBits & UTXOPrimary;
      }
      public override bool AreCollisionBitsFull()
      {
        return (MasksCollisionBitsFull[Address] & UTXOPrimary) == MasksCollisionBitsFull[Address];
      }
      public override void ResolveCollision(UTXOTable tablePrimary)
      {
        KeyValuePair<byte[], uint> collisionItem =
          CollisionTable.First(k => BitConverter.ToInt32(k.Key, 0) == tablePrimary.PrimaryKey);

        CollisionTable.Remove(collisionItem.Key);

        if (!tablePrimary.AreCollisionBitsFull() 
          || !HasCountCollisions(tablePrimary.PrimaryKey, COUNT_COLLISIONS_MAX))
        {
          tablePrimary.DecrementCollisionBits(Address);
        }

        uint uTXOPrimary = collisionItem.Value | tablePrimary.GetCollisionBits();
        PrimaryTable.Add(tablePrimary.PrimaryKey, uTXOPrimary);
      }

      protected override void SpendCollisionUTXO(
        byte[] key, 
        int outputIndex, 
        out bool areAllOutputpsSpent)
      {
        SpendUTXO(ref UTXOCollision, outputIndex, out areAllOutputpsSpent);
        CollisionTable[key] = UTXOCollision;
      }
      protected override bool TryGetValueInCollisionTable(byte[] key)
      {
        return CollisionTable.TryGetValue(key, out UTXOCollision);
      }
      protected override void RemoveCollision(byte[] key)
      {
        CollisionTable.Remove(key);
      }
      protected override bool HasCountCollisions(int primaryKey, uint countCollisions)
      {
        foreach (byte[] key in CollisionTable.Keys)
        {
          if(BitConverter.ToInt32(key, 0) == primaryKey)
          {
            countCollisions -= 1;
            if(countCollisions == 0)
            {
              return true; 
            }
          }
        }

        return false;
      }
      public override void DecrementCollisionBits(int tableAddress)
      {        
        if ((UTXOPrimary & MasksCollisionBitsFull[tableAddress]) 
          == MasksCollisionBitsOne[tableAddress])
        {
          UTXOPrimary &= MasksCollisionBitsClear[tableAddress];
          return;
        }
        
        if ((UTXOPrimary & MasksCollisionBitsFull[tableAddress]) 
          == MasksCollisionBitsTwo[tableAddress])
        {
          UTXOPrimary &= MasksCollisionBitsClear[tableAddress];
          UTXOPrimary |= MasksCollisionBitsOne[tableAddress];
          return;
        }

        UTXOPrimary |= MasksCollisionBitsTwo[tableAddress];
      }
      protected override void UpdateUTXOInTable()
      {
        PrimaryTable[PrimaryKey] = UTXOPrimary;
      }

      static void SpendUTXO(ref uint uTXO, int outputIndex, out bool areAllOutputpsSpent)
      {
        uint mask = (uint)1 << (CountNonOutputBits + outputIndex);
        if ((uTXO & mask) != 0x00)
        {
          throw new UTXOException(string.Format(
            "Output index {0} already spent.", outputIndex));
        }
        uTXO |= mask;

        areAllOutputpsSpent = (uTXO & MaskAllOutputBitsSpent) == MaskAllOutputBitsSpent;
      }

      protected override byte[] GetPrimaryData()
      {
        byte[] buffer = new byte[PrimaryTable.Count << 3];

        int index = 0;
        foreach(KeyValuePair<int, uint> keyValuePair in PrimaryTable)
        {
          BitConverter.GetBytes(keyValuePair.Key).CopyTo(buffer, index);
          index += 4;
          BitConverter.GetBytes(keyValuePair.Value).CopyTo(buffer, index);
          index += 4;
        }

        return buffer;
      }
      protected override byte[] GetCollisionData()
      {
        byte[] buffer = new byte[CollisionTable.Count * (HASH_BYTE_SIZE + 4)];

        int index = 0;
        foreach (KeyValuePair<byte[], uint> keyValuePair in CollisionTable)
        {
          keyValuePair.Key.CopyTo(buffer, index);
          index += HASH_BYTE_SIZE;
          BitConverter.GetBytes(keyValuePair.Value).CopyTo(buffer, index);
          index += 4;
        }

        return buffer;
      }

      protected override void LoadPrimaryData(byte[] buffer)
      {
        int index = 0;

        while(index < buffer.Length)
        {
          int key = BitConverter.ToInt32(buffer, index);
          index += 4;
          uint value = BitConverter.ToUInt32(buffer, index);
          index += 4;

          PrimaryTable.Add(key, value);
        }
      }
      protected override void LoadCollisionData(byte[] buffer)
      {
        int index = 0;

        while (index < buffer.Length)
        {
          byte[] key = new byte[HASH_BYTE_SIZE];
          Array.Copy(buffer, index, key, 0, HASH_BYTE_SIZE);
          index += HASH_BYTE_SIZE;

          uint value = BitConverter.ToUInt32(buffer, index);
          index += 4;

          CollisionTable.Add(key, value);
        }
      }

      public override void Clear()
      {
        PrimaryTable.Clear();
        CollisionTable.Clear();
      }

      public override bool TryParseUTXO(
        int batchIndex,
        byte[] headerHash, 
        int lengthUTXOBits,
        out UTXOItem item)
      {
        if(COUNT_INTEGER_BITS < lengthUTXOBits)
        {
          item = null;
          return false;
        }

        uint uTXOIndex = (uint)batchIndex & MaskBatchIndex;
        uTXOIndex |= ((uint)headerHash[0] << COUNT_BATCHINDEX_BITS) & MaskHeaderBits;
        
        if (COUNT_INTEGER_BITS > lengthUTXOBits)
        {
          uTXOIndex |= (uint.MaxValue << lengthUTXOBits);
        }

        item = new UTXOItemUInt32
          {
            UTXOIndex = uTXOIndex
          };

        return true;
      }
    }

    class UTXOItemUInt32 : UTXOItem
    {
      public uint UTXOIndex;
    }
  }
}
