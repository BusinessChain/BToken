using System;
using System.Collections.Generic;
using System.Linq;

namespace BToken.Accounting
{
  public partial class UTXO
  {
    class UTXOTableUInt32Array : UTXOTable
    {
      Dictionary<int, uint[]> PrimaryTable = new Dictionary<int, uint[]>();
      Dictionary<byte[], uint[]> CollisionTable =
        new Dictionary<byte[], uint[]>(new EqualityComparerByteArray());

      uint[] UTXOPrimary;
      uint[] UTXOCollision;
      
      uint[] MasksCollisionBitsClear = {
        0xFFCFFFFF,
        0xFF3FFFFF,
        0xFCFFFFFF };
      uint[] MasksCollisionBitsOne = {
        0x00100000,
        0x00400000,
        0x01000000 };
      uint[] MasksCollisionBitsTwo = {
        0x00200000,
        0x00800000,
        0x02000000 };
      uint[] MasksCollisionBitsFull = {
        0x00300000,
        0x00C00000,
        0x03000000 };

      static readonly uint MaskBatchIndex = ~(uint.MaxValue << COUNT_BATCHINDEX_BITS);
      static readonly uint MaskHeaderBits =
        ~((uint.MaxValue << (COUNT_BATCHINDEX_BITS + COUNT_HEADER_BITS)) | MaskBatchIndex);

      static readonly uint MaskAllOutputsBitsInFirstUInt32 = uint.MaxValue << CountNonOutputBits;


      public UTXOTableUInt32Array() 
        : base(2, "UInt32Array")
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
        uint collisionBits = PrimaryTable[primaryKey][0] & MasksCollisionBitsFull[collisionAddress];
        if (collisionBits == 0)
        {
          PrimaryTable[primaryKey][0] |= MasksCollisionBitsOne[collisionAddress];
          return;
        }

        if (collisionBits == MasksCollisionBitsOne[collisionAddress])
        {
          PrimaryTable[primaryKey][0] &= MasksCollisionBitsClear[collisionAddress];
          PrimaryTable[primaryKey][0] |= MasksCollisionBitsTwo[collisionAddress];
          return;
        }

        if (collisionBits == MasksCollisionBitsTwo[collisionAddress])
        {
          PrimaryTable[primaryKey][0] |= MasksCollisionBitsFull[collisionAddress];
        }
      }
      public override void SecondaryTableAddUTXO(UTXOItem uTXODataItem)
      {
        CollisionTable.Add(
          uTXODataItem.Hash,
          ((UTXOItemUInt32Array)uTXODataItem).UTXOIndex);
      }
      public override void PrimaryTableAddUTXO(UTXOItem uTXODataItem)
      {
        PrimaryTable.Add(
          uTXODataItem.PrimaryKey,
          ((UTXOItemUInt32Array)uTXODataItem).UTXOIndex);
      }

      public override void SpendPrimaryUTXO(TXInput input, out bool areAllOutputpsSpent)
      {
        SpendUTXO(UTXOPrimary, input.OutputIndex, out areAllOutputpsSpent);
      }
      public override bool TryGetValueInPrimaryTable(int primaryKey)
      {
        PrimaryKey = primaryKey; // cache
        return PrimaryTable.TryGetValue(primaryKey, out UTXOPrimary);
      }
      public override bool HasCollision(int cacheAddress)
      {
        return (MasksCollisionBitsFull[cacheAddress] & UTXOPrimary[0]) != 0;
      }
      public override void RemovePrimary()
      {
        PrimaryTable.Remove(PrimaryKey);
      }
      public override uint GetCollisionBits()
      {
        return MaskCollisionBits & UTXOPrimary[0];
      }
      public override bool AreCollisionBitsFull()
      {
        return (MasksCollisionBitsFull[Address] & UTXOPrimary[0]) 
          == MasksCollisionBitsFull[Address];
      }
      public override void ResolveCollision(UTXOTable tablePrimary)
      {
        KeyValuePair<byte[], uint[]> collisionItem =
          CollisionTable.First(k => BitConverter.ToInt32(k.Key, 0) == tablePrimary.PrimaryKey);

        CollisionTable.Remove(collisionItem.Key);

        if (!tablePrimary.AreCollisionBitsFull() ||
          !HasCountCollisions(tablePrimary.PrimaryKey, COUNT_COLLISIONS_MAX))
        {
          tablePrimary.DecrementCollisionBits(Address);
        }

        collisionItem.Value[0] |= tablePrimary.GetCollisionBits();
        PrimaryTable.Add(tablePrimary.PrimaryKey, collisionItem.Value);
      }

      protected override void SpendCollisionUTXO(byte[] key, int outputIndex, out bool areAllOutputpsSpent)
      {
        SpendUTXO(UTXOCollision, outputIndex, out areAllOutputpsSpent);
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
        if ((UTXOPrimary[0] & MasksCollisionBitsFull[tableAddress])
          == MasksCollisionBitsOne[tableAddress])
        {
          UTXOPrimary[0] &= MasksCollisionBitsClear[tableAddress];
          return;
        }

        if ((UTXOPrimary[0] & MasksCollisionBitsFull[tableAddress])
          == MasksCollisionBitsTwo[tableAddress])
        {
          UTXOPrimary[0] &= MasksCollisionBitsClear[tableAddress];
          UTXOPrimary[0] |= MasksCollisionBitsOne[tableAddress];
          return;
        }

        UTXOPrimary[0] |= MasksCollisionBitsTwo[tableAddress];
      }
      protected override void UpdateUTXOInTable()
      { }

      static void SpendUTXO(
        uint[] uTXO, 
        int outputIndex, 
        out bool areAllOutputpsSpent)
      {
        int bitOffset = CountNonOutputBits + outputIndex;
        int uintIndex = bitOffset / 32;
        int bitIndex = bitOffset % 32;

        uint mask = (uint)1 << bitIndex;
        if ((uTXO[uintIndex] & mask) != 0x00)
        {
          throw new UTXOException(string.Format(
            "Output index {0} already spent.", outputIndex));
        }
        uTXO[uintIndex] |= mask;

        areAllOutputpsSpent = AreAllOutputBitsSpent(uTXO);
      }
      static bool AreAllOutputBitsSpent(uint[] uTXO)
      {
        if ((uTXO[0] & MaskAllOutputsBitsInFirstUInt32) != MaskAllOutputsBitsInFirstUInt32)
        {
          return false;
        }
        for(int intIndex = 1; intIndex < uTXO.Length; intIndex += 1)
        {
          if (uTXO[intIndex] != uint.MaxValue)
          {
            return false;
          }
        }

        return true;
      }

      protected override byte[] GetPrimaryData()
      {
        var byteList = new List<byte>();

        foreach (KeyValuePair<int, uint[]> keyValuePair in PrimaryTable)
        {
          byteList.AddRange(BitConverter.GetBytes(keyValuePair.Key));
          byteList.AddRange(VarInt.GetBytes(keyValuePair.Value.Length));

          byte[] byteArray = new byte[keyValuePair.Value.Length << 2];
          Buffer.BlockCopy(keyValuePair.Value, 0, byteArray, 0, byteArray.Length);
          byteList.AddRange(byteArray);
        }

        return byteList.ToArray();
      }
      protected override byte[] GetCollisionData()
      {
        var byteList = new List<byte>();

        foreach (KeyValuePair<byte[], uint[]> keyValuePair in CollisionTable)
        {
          byteList.AddRange(keyValuePair.Key);
          byteList.AddRange(VarInt.GetBytes(keyValuePair.Value.Length));

          byte[] byteArray = new byte[keyValuePair.Value.Length << 2];
          Buffer.BlockCopy(keyValuePair.Value, 0, byteArray, 0, byteArray.Length);
          byteList.AddRange(byteArray);

        }

        return byteList.ToArray();
      }

      protected override void LoadPrimaryData(byte[] buffer)
      {
        //int index = 0;

        //int key;
        //int lengthValue;
        //byte[] value;

        //try
        //{
        //  while (index < buffer.Length)
        //  {
        //    key = BitConverter.ToInt32(buffer, index);
        //    index += 4;

        //    lengthValue = VarInt.GetInt32(buffer, ref index);
        //    value = new byte[lengthValue];
        //    Array.Copy(buffer, index, value, 0, lengthValue);
        //    index += lengthValue;

        //    PrimaryTable.Add(key, value);
        //  }
        //}
        //catch (Exception ex)
        //{
        //  Console.WriteLine(ex.Message);
        //}
      }
      protected override void LoadCollisionData(byte[] buffer)
      {
        //int index = 0;

        //while (index < buffer.Length)
        //{
        //  byte[] key = new byte[HASH_BYTE_SIZE];
        //  Array.Copy(buffer, index, key, 0, HASH_BYTE_SIZE);
        //  index += HASH_BYTE_SIZE;

        //  int lengthValue = VarInt.GetInt32(buffer, ref index);
        //  byte[] value = new byte[lengthValue];
        //  Array.Copy(buffer, index, value, 0, lengthValue);
        //  index += lengthValue;

        //  CollisionTable.Add(key, value);
        //}
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
        uint[] uTXOIndex = new uint[(lengthUTXOBits + 31) / 32];

        int countUTXORemainderBits = lengthUTXOBits % 32;
        if (countUTXORemainderBits > 0)
        {
          uTXOIndex[uTXOIndex.Length - 1] |= (uint.MaxValue << countUTXORemainderBits);
        }
        
        uTXOIndex[0] = (uint)batchIndex & MaskBatchIndex;
        uTXOIndex[0] |= ((uint)headerHash[0] << COUNT_BATCHINDEX_BITS) & MaskHeaderBits;
                
        item = new UTXOItemUInt32Array
        {
          UTXOIndex = uTXOIndex
        };

        return true;
      }
    }

    class UTXOItemUInt32Array : UTXOItem
    {
      public uint[] UTXOIndex;
    }
  }
}
