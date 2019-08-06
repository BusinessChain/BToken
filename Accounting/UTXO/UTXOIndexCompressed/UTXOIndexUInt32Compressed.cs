using System;
using System.Collections.Generic;
using System.Linq;
using System.Diagnostics;

namespace BToken.Accounting
{
  public partial class UTXO
  {
    class UTXOIndexUInt32Compressed : UTXOIndexCompressed
    {
      public Dictionary<int, uint> PrimaryTable = new Dictionary<int, uint>();
      const int COUNT_COLLISION_TABLE_PARTITIONS = 256;
      public Dictionary<byte[], uint>[] CollisionTables = new Dictionary<byte[], uint>[COUNT_COLLISION_TABLE_PARTITIONS];

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

      public UTXOIndexUInt32Compressed()
        : base(0, "UInt32")
      {
        for(int i = 0; i < COUNT_COLLISION_TABLE_PARTITIONS; i += 1)
        {
          CollisionTables[i] = new Dictionary<byte[], uint>(new EqualityComparerByteArray());
        }
      }

      protected override int GetCountPrimaryTableItems()
      {
        return PrimaryTable.Count;
      }
      protected override int GetCountCollisionTableItems()
      {
        return CollisionTables.Sum(t => t.Count);
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
          return;
        }
      }

      public override void SpendPrimaryUTXO(in TXInput input, out bool areAllOutputpsSpent)
      {
        SpendUTXO(ref UTXOPrimary, input.OutputIndex, out areAllOutputpsSpent);
        PrimaryTable[PrimaryKey] = UTXOPrimary;
      }
      public override bool TryGetValueInPrimaryTable(int primaryKey)
      {
        PrimaryKey = primaryKey;
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
      public override void ResolveCollision(UTXOIndexCompressed tablePrimary)
      {
        KeyValuePair<byte[], uint> collisionItem =
          CollisionTables[(byte)tablePrimary.PrimaryKey].First(k => BitConverter.ToInt32(k.Key, 0) == tablePrimary.PrimaryKey);

        CollisionTables[(byte)tablePrimary.PrimaryKey].Remove(collisionItem.Key);

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
        CollisionTables[key[0]][key] = UTXOCollision;
      }
      protected override bool TryGetValueInCollisionTable(byte[] key)
      {
        return CollisionTables[key[0]].TryGetValue(key, out UTXOCollision);
      }
      protected override void RemoveCollision(byte[] key)
      {
        CollisionTables[key[0]].Remove(key);
      }
      protected override bool HasCountCollisions(int primaryKey, uint countCollisions)
      {
        foreach (byte[] key in CollisionTables[(byte)primaryKey].Keys)
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

        UTXOPrimary &= MasksCollisionBitsClear[tableAddress];
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
        foreach (KeyValuePair<int, uint> keyValuePair in PrimaryTable)
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
        byte[] buffer = new byte[GetCountCollisionTableItems() * (HASH_BYTE_SIZE + 4)];

        for(int i = 0; i < COUNT_COLLISION_TABLE_PARTITIONS; i += 1)
        {
          int index = 0;
          foreach (KeyValuePair<byte[], uint> keyValuePair in CollisionTables[i])
          {
            keyValuePair.Key.CopyTo(buffer, index);
            index += HASH_BYTE_SIZE;
            BitConverter.GetBytes(keyValuePair.Value).CopyTo(buffer, index);
            index += 4;
          }
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

          CollisionTables[key[0]].Add(key, value);
        }
      }

      public override void Clear()
      {
        PrimaryTable.Clear();
        CollisionTables.ToList().ForEach(t => t.Clear());
      }
    }
  }
}
