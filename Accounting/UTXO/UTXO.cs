using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

using BToken.Chaining;
using BToken.Networking;

namespace BToken.Accounting
{
  public partial class UTXO
  {
    Headerchain Headerchain;
    Network Network;

    UTXOBuilder Builder;

    static string PathUTXOState = "UTXO";
    static string PathUTXOStateTemporary = PathUTXOState + "_temp";
    static string PathUTXOStateOld = PathUTXOState + "_Old";

    const int COUNT_INTEGER_BITS = 32;
    const int COUNT_LONG_BITS = 64;

    const int HASH_BYTE_SIZE = 32;

    const int COUNT_BATCHINDEX_BITS = 16;
    const int COUNT_HEADER_BITS = 4;
    const int COUNT_COLLISION_BITS_PER_TABLE = 2;
    const int COUNT_COLLISIONS_MAX = 3;

    const int COUNT_TXS_IN_BATCH_FILE = 10000;

    UTXOIndexCompressed[] Tables;
    UTXOIndexUInt32Compressed TableUInt32 = new UTXOIndexUInt32Compressed();
    UTXOIndexULong64Compressed TableULong64 = new UTXOIndexULong64Compressed();
    UTXOIndexUInt32ArrayCompressed TableUInt32Array = new UTXOIndexUInt32ArrayCompressed();
    
    static readonly int CountNonOutputBits =
      COUNT_BATCHINDEX_BITS +
      COUNT_HEADER_BITS +
      COUNT_COLLISION_BITS_PER_TABLE * 3;
        

    public UTXO(
      BitcoinGenesisBlock genesisBlock, 
      Headerchain headerchain, 
      Network network)
    {
      Headerchain = headerchain;
      Network = network;

      Builder = new UTXOBuilder(this, genesisBlock);

      Tables = new UTXOIndexCompressed[]{
        TableUInt32,
        TableULong64,
        TableUInt32Array};
    }

    public async Task StartAsync()
    {
      await Builder.RunAsync();
    }

    void InsertUTXOsUInt32(KeyValuePair<byte[], uint>[] uTXOsUInt32, int index)
    {
      int i = 0;

    LoopUTXOItems:
      while(i < index)
      {
        int primaryKey = BitConverter.ToInt32(uTXOsUInt32[i].Key, 0);
        
        for (int c = 0; c < Tables.Length; c += 1)
        {
          if (Tables[c].PrimaryTableContainsKey(primaryKey))
          {
            Tables[c].IncrementCollisionBits(primaryKey, 0);

            TableUInt32.CollisionTable.Add(uTXOsUInt32[i].Key, uTXOsUInt32[i].Value);

            i += 1;
            goto LoopUTXOItems;
          }
        }

        TableUInt32.PrimaryTable.Add(primaryKey, uTXOsUInt32[i].Value);

        i += 1;
      }
    }
    void InsertUTXOsULong64(KeyValuePair<byte[], ulong>[] uTXOsULong64, int index)
    {
      int i = 0;

    LoopUTXOItems:
      while (i < index)
      {
        int primaryKey = BitConverter.ToInt32(uTXOsULong64[i].Key, 0);
        
        for (int c = 0; c < Tables.Length; c += 1)
        {
          if (Tables[c].PrimaryTableContainsKey(primaryKey))
          {
            Tables[c].IncrementCollisionBits(primaryKey, 1);

            TableULong64.CollisionTable.Add(uTXOsULong64[i].Key, uTXOsULong64[i].Value);

            i += 1;
            goto LoopUTXOItems;
          }
        }

        TableULong64.PrimaryTable.Add(primaryKey, uTXOsULong64[i].Value);

        i += 1;
      }
    }
    void InsertUTXOsUInt32Array(KeyValuePair<byte[], uint[]>[] uTXOsUInt32Array, int index)
    {
      int i = 0;

    LoopUTXOItems:
      while (i < index)
      {
        int primaryKey = BitConverter.ToInt32(uTXOsUInt32Array[i].Key, 0);
        
        for (int c = 0; c < Tables.Length; c += 1)
        {
          if (Tables[c].PrimaryTableContainsKey(primaryKey))
          {
            Tables[c].IncrementCollisionBits(primaryKey, 2);

            TableUInt32Array.CollisionTable.Add(uTXOsUInt32Array[i].Key, uTXOsUInt32Array[i].Value);

            i += 1;
            goto LoopUTXOItems;
          }
        }

        TableUInt32Array.PrimaryTable.Add(primaryKey, uTXOsUInt32Array[i].Value);

        i += 1;
      }
    }

    void SpendUTXOs(TXInput[] inputs, int inputIndex)
    {
      int i = 0;
    LoopSpendUTXOs:
      while (i < inputIndex)
      {
        for (int c = 0; c < Tables.Length; c += 1)
        {
          UTXOIndexCompressed tablePrimary = Tables[c];

          if (tablePrimary.TryGetValueInPrimaryTable(inputs[i].PrimaryKeyTXIDOutput))
          {
            UTXOIndexCompressed tableCollision = null;
            for (int cc = 0; cc < Tables.Length; cc += 1)
            {
              if (tablePrimary.HasCollision(cc))
              {
                tableCollision = Tables[cc];

                if (tableCollision.TrySpendCollision(inputs[i], tablePrimary))
                {
                  i += 1;
                  goto LoopSpendUTXOs;
                }
              }
            }

            tablePrimary.SpendPrimaryUTXO(inputs[i], out bool allOutputsSpent);

            if (allOutputsSpent)
            {
              tablePrimary.RemovePrimary();

              if (tableCollision != null)
              {
                tableCollision.ResolveCollision(tablePrimary);
              }
            }

            i += 1;
            goto LoopSpendUTXOs;
          }
        }

        throw new UTXOException(string.Format(
          "Referenced TX {0} not found in UTXO table.",
          inputs[i].TXIDOutput.ToHexString()));
      }
    }
        
  }
}