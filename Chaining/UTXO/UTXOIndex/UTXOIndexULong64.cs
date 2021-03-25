using System;
using System.Collections.Generic;
using System.Linq;
using System.IO;

namespace BToken.Chaining
{
  partial class UTXOTable
  {
    class UTXOIndexULong64 : UTXOIndex
    {
      public Dictionary<int, ulong> PrimaryTable = new Dictionary<int, ulong>();
      public Dictionary<byte[], ulong> CollisionTable =
        new Dictionary<byte[], ulong>(new EqualityComparerByteArray());

      ulong UTXOPrimary;
      ulong UTXOCollision;

      const int COUNT_LONG_BITS = 64;

      static readonly ulong MaskAllOutputBitsSpent = ulong.MaxValue << COUNT_NON_OUTPUT_BITS;

      ulong[] MasksCollisionBitsClear = {
        ~(ulong)(COUNT_COLLISIONS_MAX << COUNT_BATCHINDEX_BITS + COUNT_COLLISION_BITS_PER_TABLE * 0),
        ~(ulong)(COUNT_COLLISIONS_MAX << COUNT_BATCHINDEX_BITS + COUNT_COLLISION_BITS_PER_TABLE * 1),
        ~(ulong)(COUNT_COLLISIONS_MAX << COUNT_BATCHINDEX_BITS + COUNT_COLLISION_BITS_PER_TABLE * 2)};
      ulong[] MasksCollisionBitsOne = {
        1 << COUNT_BATCHINDEX_BITS + COUNT_COLLISION_BITS_PER_TABLE * 0,
        1 << COUNT_BATCHINDEX_BITS + COUNT_COLLISION_BITS_PER_TABLE * 1,
        1 << COUNT_BATCHINDEX_BITS + COUNT_COLLISION_BITS_PER_TABLE * 2};
      ulong[] MasksCollisionBitsTwo = {
        2 << COUNT_BATCHINDEX_BITS + COUNT_COLLISION_BITS_PER_TABLE * 0,
        2 << COUNT_BATCHINDEX_BITS + COUNT_COLLISION_BITS_PER_TABLE * 1,
        2 << COUNT_BATCHINDEX_BITS + COUNT_COLLISION_BITS_PER_TABLE * 2};
      ulong[] MasksCollisionBitsFull = {
        3 << (COUNT_BATCHINDEX_BITS + COUNT_COLLISION_BITS_PER_TABLE * 0),
        3 << COUNT_BATCHINDEX_BITS + COUNT_COLLISION_BITS_PER_TABLE * 1,
        3 << COUNT_BATCHINDEX_BITS + COUNT_COLLISION_BITS_PER_TABLE * 2};

      public UTXOIndexULong64()
        : base(1, "ULong64")
      { }

      protected override int GetCountPrimaryTableItems()
      {
        return PrimaryTable.Count;
      }
      protected override int GetCountCollisionTableItems()
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

      public override void SpendPrimaryUTXO(in TXInput input, out bool areAllOutputpsSpent)
      {
        SpendUTXO(ref UTXOPrimary, input.OutputIndex, out areAllOutputpsSpent);
        PrimaryTable[input.TXIDOutputShort] = UTXOPrimary;
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
      public override void ResolveCollision(UTXOIndex tablePrimary)
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

      public ulong UTXO;
      public override void AddUTXOAsCollision(byte[] uTXOKey)
      {
        CollisionTable.Add(uTXOKey, UTXO);
      }
      public override void AddUTXOAsPrimary(int primaryKey)
      {
        PrimaryTable.Add(primaryKey, UTXO);
      }

      protected override void SpendCollisionUTXO(byte[] key, int outputIndex, out bool areAllOutputpsSpent)
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
        ulong mask = (ulong)1 << (COUNT_NON_OUTPUT_BITS + outputIndex);
        if ((uTXO & mask) != 0x00)
        {
          throw new ProtocolException(
            string.Format(
              "Output index {0} already spent.",
              outputIndex),
            ErrorCode.INVALID);
        }
        uTXO |= mask;

        areAllOutputpsSpent = (uTXO & MaskAllOutputBitsSpent) == MaskAllOutputBitsSpent;
      }

      public override void BackupImage(string path)
      {
        string directoryPath = Path.Combine(path, Label);
        Directory.CreateDirectory(directoryPath);

        using (FileStream stream = new FileStream(
           Path.Combine(directoryPath, "PrimaryTable"),
           FileMode.Create,
           FileAccess.Write,
           FileShare.None))
        {
          foreach (KeyValuePair<int, ulong> keyValuePair in PrimaryTable)
          {
            stream.Write(BitConverter.GetBytes(keyValuePair.Key), 0, 4);
            stream.Write(BitConverter.GetBytes(keyValuePair.Value), 0, 8);
          }
        }

        using (FileStream stream = new FileStream(
           Path.Combine(directoryPath, "CollisionTable"),
           FileMode.Create,
           FileAccess.Write,
           FileShare.None))
        {
          foreach (KeyValuePair<byte[], ulong> keyValuePair in CollisionTable)
          {
            stream.Write(keyValuePair.Key, 0, HASH_BYTE_SIZE);
            stream.Write(BitConverter.GetBytes(keyValuePair.Value), 0, 8);
          }
        }
      }
      public override void LoadImage(string path)
      {
        byte[] buffer = File.ReadAllBytes(
          Path.Combine(path, Label, "PrimaryTable"));

        int index = 0;

        while (index < buffer.Length)
        {
          int key = BitConverter.ToInt32(buffer, index);
          index += 4;
          ulong value = BitConverter.ToUInt64(buffer, index);
          index += 8;

          PrimaryTable.Add(key, value);
        }

        buffer = File.ReadAllBytes(
          Path.Combine(path, Label, "CollisionTable"));

        index = 0;

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
  }
}
