using System;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Diagnostics;

namespace BToken.Chaining
{
  partial class UTXOTable
  {
    class UTXOIndexUInt32 : UTXOIndex
    {
      const int COUNT_TABLE_PARTITIONS_MEMORY = 256;
      const int COUNT_TABLE_PARTITIONS_FILE = 16;

      public Dictionary<int, uint>[] PrimaryTables = 
        new Dictionary<int, uint>[COUNT_TABLE_PARTITIONS_MEMORY];

      public Dictionary<byte[], uint>[] CollisionTables = 
        new Dictionary<byte[], uint>[COUNT_TABLE_PARTITIONS_MEMORY];

      uint UTXOPrimary;
      uint UTXOCollision;

      const int COUNT_INTEGER_BITS = 32;

      static readonly uint MaskAllOutputBitsSpent = uint.MaxValue << COUNT_NON_OUTPUT_BITS;

      uint[] MasksCollisionBitsClear = {
        ~(uint)(COUNT_COLLISIONS_MAX << COUNT_BATCHINDEX_BITS + COUNT_COLLISION_BITS_PER_TABLE * 0),
        ~(uint)(COUNT_COLLISIONS_MAX << COUNT_BATCHINDEX_BITS + COUNT_COLLISION_BITS_PER_TABLE * 1),
        ~(uint)(COUNT_COLLISIONS_MAX << COUNT_BATCHINDEX_BITS + COUNT_COLLISION_BITS_PER_TABLE * 2)};

      uint[] MasksCollisionBitsOne = {
        1 << COUNT_BATCHINDEX_BITS + COUNT_COLLISION_BITS_PER_TABLE * 0,
        1 << COUNT_BATCHINDEX_BITS + COUNT_COLLISION_BITS_PER_TABLE * 1,
        1 << COUNT_BATCHINDEX_BITS + COUNT_COLLISION_BITS_PER_TABLE * 2};
      uint[] MasksCollisionBitsTwo = {
        2 << COUNT_BATCHINDEX_BITS + COUNT_COLLISION_BITS_PER_TABLE * 0,
        2 << COUNT_BATCHINDEX_BITS + COUNT_COLLISION_BITS_PER_TABLE * 1,
        2 << COUNT_BATCHINDEX_BITS + COUNT_COLLISION_BITS_PER_TABLE * 2};
      uint[] MasksCollisionBitsFull = {
        3 << COUNT_BATCHINDEX_BITS + COUNT_COLLISION_BITS_PER_TABLE * 0,
        3 << COUNT_BATCHINDEX_BITS + COUNT_COLLISION_BITS_PER_TABLE * 1,
        3 << COUNT_BATCHINDEX_BITS + COUNT_COLLISION_BITS_PER_TABLE * 2};

      public UTXOIndexUInt32()
        : base(0, "UInt32")
      {
        for (int i = 0; i < COUNT_TABLE_PARTITIONS_MEMORY; i += 1)
        {
          CollisionTables[i] = new Dictionary<byte[], uint>(
            new EqualityComparerByteArray());

          PrimaryTables[i] = new Dictionary<int, uint>();
        }
      }

      protected override int GetCountPrimaryTableItems()
      {
        return PrimaryTables.Sum(t => t.Count);
      }

      protected override int GetCountCollisionTableItems()
      {
        return CollisionTables.Sum(t => t.Count);
      }

      public override bool PrimaryTableContainsKey(
        int primaryKey)
      {
        return PrimaryTables[(byte)primaryKey]
          .ContainsKey(primaryKey);
      }

      public override void IncrementCollisionBits(
        int primaryKey, int collisionAddress)
      {
        byte indexTablePartition = (byte)primaryKey;

        uint collisionBits = 
          PrimaryTables [indexTablePartition][primaryKey] & 
          MasksCollisionBitsFull[collisionAddress];

        if (collisionBits == 0)
        {
          PrimaryTables[indexTablePartition][primaryKey] |= 
            MasksCollisionBitsOne[collisionAddress];
          return;
        }

        if (collisionBits == MasksCollisionBitsOne[collisionAddress])
        {
          PrimaryTables[indexTablePartition][primaryKey] &= 
            MasksCollisionBitsClear[collisionAddress];

          PrimaryTables[indexTablePartition][primaryKey] |= 
            MasksCollisionBitsTwo[collisionAddress];

          return;
        }

        if (collisionBits == MasksCollisionBitsTwo[collisionAddress])
        {
          PrimaryTables[indexTablePartition][primaryKey] |= MasksCollisionBitsFull[collisionAddress];
          return;
        }
      }

      public override void SpendPrimaryUTXO(in TXInput input, out bool areAllOutputpsSpent)
      {
        SpendUTXO(ref UTXOPrimary, input.OutputIndex, out areAllOutputpsSpent);
        PrimaryTables[(byte)PrimaryKey][PrimaryKey] = UTXOPrimary;
      }

      public override bool TryGetValueInPrimaryTable(int primaryKey)
      {
        PrimaryKey = primaryKey;

        return PrimaryTables[(byte)PrimaryKey].TryGetValue(
          primaryKey, 
          out UTXOPrimary);
      }

      public override bool HasCollision(int tableAddress)
      {
        return 
          (MasksCollisionBitsFull[tableAddress] & UTXOPrimary) 
          != 0;
      }

      public override void RemovePrimary()
      {
        PrimaryTables[(byte)PrimaryKey].Remove(PrimaryKey);
      }

      public override uint GetCollisionBits()
      {
        return MaskCollisionBits & UTXOPrimary;
      }

      public override bool AreCollisionBitsFull()
      {
        return (MasksCollisionBitsFull[Address] & UTXOPrimary) == 
          MasksCollisionBitsFull[Address];
      }

      public override void ResolveCollision(
        UTXOIndex tablePrimary)
      {
        byte indexTablePartition = (byte)tablePrimary.PrimaryKey;

        KeyValuePair<byte[], uint> collisionItem =
          CollisionTables[indexTablePartition]
          .First(k => BitConverter.ToInt32(k.Key, 0) == tablePrimary.PrimaryKey);

        CollisionTables[indexTablePartition].Remove(collisionItem.Key);

        if (!tablePrimary.AreCollisionBitsFull()
          || !HasCountCollisions(tablePrimary.PrimaryKey, COUNT_COLLISIONS_MAX))
        {
          tablePrimary.DecrementCollisionBits(Address);
        }

        uint uTXOPrimary = 
          collisionItem.Value | tablePrimary.GetCollisionBits();

        PrimaryTables[indexTablePartition].Add(
          tablePrimary.PrimaryKey, 
          uTXOPrimary);
      }

      public uint UTXO;
      public override void AddUTXOAsCollision(byte[] uTXOKey)
      {
        CollisionTables[uTXOKey[0]].Add(uTXOKey, UTXO);
      }
      public override void AddUTXOAsPrimary(int primaryKey)
      {
        PrimaryTables[(byte)primaryKey].Add(primaryKey, UTXO);
      }

      protected override void SpendCollisionUTXO(
        byte[] key,
        int outputIndex,
        out bool areAllOutputpsSpent)
      {
        SpendUTXO(
          ref UTXOCollision, 
          outputIndex, 
          out areAllOutputpsSpent);

        CollisionTables[key[0]][key] = UTXOCollision;
      }
      protected override bool TryGetValueInCollisionTable(
        byte[] key)
      {
        return CollisionTables[key[0]]
          .TryGetValue(key, out UTXOCollision);
      }
      protected override void RemoveCollision(byte[] key)
      {
        CollisionTables[key[0]].Remove(key);
      }
      protected override bool HasCountCollisions(int primaryKey, uint countCollisions)
      {
        foreach (byte[] key in CollisionTables[(byte)primaryKey].Keys)
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

        UTXOPrimary &= MasksCollisionBitsClear[tableAddress];
        UTXOPrimary |= MasksCollisionBitsTwo[tableAddress];
      }
      protected override void UpdateUTXOInTable()
      {
        PrimaryTables[(byte)PrimaryKey][PrimaryKey] = UTXOPrimary;
      }

      static void SpendUTXO(
        ref uint uTXO,
        int outputIndex,
        out bool areAllOutputpsSpent)
      {
        uint mask = (uint)1 << (COUNT_NON_OUTPUT_BITS + outputIndex);
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

      Stopwatch Stopwatch = new Stopwatch();

      void WriteFile(int i, string directoryPath)
      {
        using (FileStream stream = new FileStream(
           Path.Combine(directoryPath, "PrimaryTable" + i),
           FileMode.Create,
           FileAccess.Write,
           FileShare.None,
           bufferSize: 65536))
        {
          for (
            int n = i;
            n < COUNT_TABLE_PARTITIONS_MEMORY;
            n += COUNT_TABLE_PARTITIONS_FILE)
          {
            var keyValuePairs = PrimaryTables[n].ToArray();

            byte[] bytesLengthArrayKeyValuePairs =
            VarInt.GetBytes(keyValuePairs.Length).ToArray();

            stream.Write(
              bytesLengthArrayKeyValuePairs,
              0,
              bytesLengthArrayKeyValuePairs.Length);

            for (int k = 0; k < keyValuePairs.Length; k += 1)
            {
              stream.Write(
                BitConverter.GetBytes(keyValuePairs[k].Key),
                0,
                4);

              stream.Write(
                BitConverter.GetBytes(keyValuePairs[k].Value),
                0,
                4);
            }
          }
        }
      }

      public override void BackupImage(string path)
      {
        string directoryPath = Path.Combine(path, Label);
        Directory.CreateDirectory(directoryPath);

        Stopwatch.Restart();

        Parallel.For(
          0, 
          COUNT_TABLE_PARTITIONS_FILE, 
          i => WriteFile(i, directoryPath));

        Stopwatch.Stop();

        Console.WriteLine(
          "Time store image {0}", 
          Stopwatch.ElapsedMilliseconds);

        byte[] bufferCollisionTable = new byte[
          GetCountCollisionTableItems() * (HASH_BYTE_SIZE + sizeof(uint))];
        int index = 0;

        for (int i = 0; i < COUNT_TABLE_PARTITIONS_MEMORY; i += 1)
        {
          foreach (KeyValuePair<byte[], uint> keyValuePair in CollisionTables[i])
          {
            keyValuePair.Key.CopyTo(bufferCollisionTable, index);
            index += HASH_BYTE_SIZE;
            BitConverter.GetBytes(keyValuePair.Value)
              .CopyTo(bufferCollisionTable, index);
            index += 4;
          }
        }

        using (FileStream stream = new FileStream(
           Path.Combine(directoryPath, "CollisionTable"),
           FileMode.Create,
           FileAccess.Write,
           FileShare.None))
        {
          stream.Write(
            bufferCollisionTable, 
            0, 
            bufferCollisionTable.Length);
        }
      }


      public override void LoadImage(string path)
      {
        int exponentCountLoadersPartition = 2;
        Debug.Assert(exponentCountLoadersPartition < 8);

        int countLoadersPartition =
          (int)Math.Pow(
            2,
            exponentCountLoadersPartition);

        Parallel.For(0, countLoadersPartition, n => 
        RunLoaderPartition(
          n, 
          countLoadersPartition, 
          path));

        Console.WriteLine("Load collision table.");

        LoadCollisionData(File.ReadAllBytes(
          Path.Combine(path, Label, "CollisionTable")));
      }

      void RunLoaderPartition(
        int indexLoaderPartition,
        int countLoadersPartition,
        string path)
      {
        var buffer = new byte[4];

        for (
          int i = indexLoaderPartition;
          i < COUNT_TABLE_PARTITIONS_FILE;
          i += countLoadersPartition)
        {
          Console.WriteLine("Loader {0} loads primary table {1}.",
            Thread.CurrentThread.ManagedThreadId,
            i);

          using (FileStream stream = new FileStream(
             Path.Combine(path, Label, "PrimaryTable" + i),
             FileMode.Open,
             FileAccess.Read,
             FileShare.None,
             bufferSize: 65536))
          {
            for (
              int p = i;
              p < COUNT_TABLE_PARTITIONS_MEMORY;
              p += COUNT_TABLE_PARTITIONS_FILE)
            {
              int countItemsPartition = VarInt.GetInt32(
                stream,
                buffer);

              while (countItemsPartition > 0)
              {
                stream.Read(buffer, 0, 4);
                int key = BitConverter.ToInt32(buffer, 0);

                stream.Read(buffer, 0, 4);
                uint value = BitConverter.ToUInt32(buffer, 0);

                PrimaryTables[p].Add(key, value);

                countItemsPartition -= 1;
              }
            }
          }
        }
      }

      void LoadCollisionData(byte[] buffer)
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
        PrimaryTables.ToList().ForEach(t => t.Clear());
        CollisionTables.ToList().ForEach(t => t.Clear());
      }
    }
  }
}
