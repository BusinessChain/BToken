using System;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using System.Threading.Tasks;

namespace BToken.Accounting
{
  public partial class UTXO
  {
    class UTXOTableULong64 : UTXOTable
    {
      Dictionary<int, ulong> PrimaryTable = new Dictionary<int, ulong>();
      Dictionary<byte[], ulong> CollisionTable =
        new Dictionary<byte[], ulong>(new EqualityComparerByteArray());

      ulong UTXOPrimary;
      ulong UTXOSecondary;

      const int COUNT_LONG_BITS = 64;

      static readonly ulong MaskAllOutputBitsSpent = ulong.MaxValue << CountNonOutputBits;
      static readonly ulong MaskBatchIndex = ~(ulong.MaxValue << COUNT_BATCHINDEX_BITS);
      static readonly ulong MaskHeaderBits =
        ~((uint.MaxValue << (COUNT_BATCHINDEX_BITS + COUNT_HEADER_BITS)) | MaskBatchIndex);

      ulong[] MasksCollisionBitsClear = {
        0xFFFFFFFFFFCFFFFF,
        0xFFFFFFFFFF3FFFFF,
        0xFFFFFFFFFCFFFFFF };
      ulong[] MasksCollisionBitsOne = {
        0x00100000,
        0x00400000,
        0x01000000 };
      ulong[] MasksCollisionBitsTwo = {
        0x00200000,
        0x00800000,
        0x02000000 };
      ulong[] MasksCollisionBitsFull = {
        0x00300000,
        0x00C00000,
        0x03000000 };

      public UTXOTableULong64()
        : base(1, "ULong64")
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
        ulong collisionBits = PrimaryTable[primaryKey] & MasksCollisionBitsFull[collisionAddress];
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
      public override void SecondaryTableAddUTXO(UTXOItem uTXODataItem)
      {
        CollisionTable.Add(
          uTXODataItem.Hash,
          ((UTXOItemULong64)uTXODataItem).UTXOIndex);
      }
      public override void PrimaryTableAddUTXO(UTXOItem uTXODataItem)
      {
        PrimaryTable.Add(
          uTXODataItem.PrimaryKey,
          ((UTXOItemULong64)uTXODataItem).UTXOIndex);
      }
      
      public override bool TryParseUTXO(
        int batchIndex,
        byte[] headerHash,
        int lengthUTXOBits,
        out UTXOItem item)
      {
        if (COUNT_LONG_BITS < lengthUTXOBits)
        {
          item = null;
          return false;
        }

        ulong uTXOIndex = (uint)batchIndex & MaskBatchIndex;
        uTXOIndex |= ((uint)headerHash[0] << COUNT_BATCHINDEX_BITS) & MaskHeaderBits;
        
        if (COUNT_LONG_BITS > lengthUTXOBits)
        {
          uTXOIndex |= (ulong.MaxValue << lengthUTXOBits);
        }

        item = new UTXOItemULong64
        {
          UTXOIndex = uTXOIndex
        };

        return true;
      }

      public override void SpendPrimaryUTXO(TXInput input, out bool areAllOutputpsSpent)
      {
        SpendUTXO(ref UTXOPrimary, input.OutputIndex, out areAllOutputpsSpent);
        PrimaryTable[input.PrimaryKeyTXIDOutput] = UTXOPrimary;
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
      public override void ResolveCollision(UTXOTable tablePrimary)
      {
        KeyValuePair<byte[], ulong> collisionItem =
          CollisionTable.First(k => BitConverter.ToInt32(k.Key, 0) == tablePrimary.PrimaryKey);

        CollisionTable.Remove(collisionItem.Key);

        if (!tablePrimary.AreCollisionBitsFull()
          || !HasCountCollisions(tablePrimary.PrimaryKey, COUNT_COLLISIONS_MAX))
        {
          tablePrimary.DecrementCollisionBits(Address);
        }

        ulong uTXOPrimary = collisionItem.Value | tablePrimary.GetCollisionBits();
        PrimaryTable.Add(tablePrimary.PrimaryKey, uTXOPrimary);
      }

      public override uint GetCollisionBits()
      {
        return MaskCollisionBits & (uint)UTXOPrimary;
      }
      public override bool AreCollisionBitsFull()
      {
        return (MasksCollisionBitsFull[Address] & UTXOPrimary) 
          == MasksCollisionBitsFull[Address];
      }

      protected override void SpendCollisionUTXO(byte[] key, int outputIndex, out bool areAllOutputpsSpent)
      {
        SpendUTXO(ref UTXOSecondary, outputIndex, out areAllOutputpsSpent);
        CollisionTable[key] = UTXOSecondary;
      }
      protected override bool TryGetValueInCollisionTable(byte[] key)
      {
        return CollisionTable.TryGetValue(key, out UTXOSecondary);
      }
      protected override void RemoveCollision(byte[] key)
      {
        CollisionTable.Remove(key);
      }
      protected override bool HasCountCollisions(int primaryKey, uint countCollisions)
      {
        foreach (byte[] key in CollisionTable.Keys)
        {
          if (BitConverter.ToInt32(key, 0) == primaryKey)
          {
            countCollisions -= 1;
            if (countCollisions == 0)
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

      static void SpendUTXO(ref ulong uTXO, int outputIndex, out bool areAllOutputpsSpent)
      {
        ulong mask = (ulong)1 << (CountNonOutputBits + outputIndex);
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
        byte[] buffer = new byte[PrimaryTable.Count * 12];

        int index = 0;
        foreach (KeyValuePair<int, ulong> keyValuePair in PrimaryTable)
        {
          BitConverter.GetBytes(keyValuePair.Key).CopyTo(buffer, index);
          index += 4;
          BitConverter.GetBytes(keyValuePair.Value).CopyTo(buffer, index);
          index += 8;
        }

        return buffer;
      }
      protected override byte[] GetCollisionData()
      {
        byte[] buffer = new byte[CollisionTable.Count * (HASH_BYTE_SIZE + 8)];

        int index = 0;
        foreach (KeyValuePair<byte[], ulong> keyValuePair in CollisionTable)
        {
          keyValuePair.Key.CopyTo(buffer, index);
          index += HASH_BYTE_SIZE;
          BitConverter.GetBytes(keyValuePair.Value).CopyTo(buffer, index);
          index += 8;
        }

        return buffer;
      }


      protected override void LoadPrimaryData(byte[] buffer)
      {
        int index = 0;

        while (index < buffer.Length)
        {
          int key = BitConverter.ToInt32(buffer, index);
          index += 4;
          ulong value = BitConverter.ToUInt64(buffer, index);
          index += 8;

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

          ulong value = BitConverter.ToUInt64(buffer, index);
          index += 8;

          CollisionTable.Add(key, value);
        }
      }

      public override void Clear()
      {
        PrimaryTable.Clear();
        CollisionTable.Clear();
      }
    }

    class UTXOItemULong64 : UTXOItem
    {
      public ulong UTXOIndex;
    }
  }
}
