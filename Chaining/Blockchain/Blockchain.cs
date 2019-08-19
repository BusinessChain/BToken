using System;
using System.Collections.Generic;
using System.Linq;
using System.Diagnostics;
using System.Threading.Tasks;

using BToken.Networking;

namespace BToken.Chaining
{
  public partial class Blockchain
  {
    List<HeaderLocation> Checkpoints;
    Headerchain Chain;
    Network Network;
    UTXOMerger Merger;
    UTXOArchiveLoader ArchiveLoader;
    UTXONetworkLoader NetworkLoader;
    BitcoinGenesisBlock GenesisBlock;

    static string PathUTXOState = "UTXOArchive";
    static string PathUTXOStateOld = PathUTXOState + "_Old";

    const int COUNT_INTEGER_BITS = 32;
    const int COUNT_LONG_BITS = 64;

    const int HASH_BYTE_SIZE = 32;

    const int COUNT_BATCHINDEX_BITS = 16;
    const int COUNT_COLLISION_BITS_PER_TABLE = 2;
    const int COUNT_COLLISIONS_MAX = 2 ^ COUNT_COLLISION_BITS_PER_TABLE - 1;

    const int COUNT_TXS_IN_BATCH_FILE = 50000;

    UTXOIndexCompressed[] Tables;
    UTXOIndexUInt32Compressed TableUInt32 = new UTXOIndexUInt32Compressed();
    UTXOIndexULong64Compressed TableULong64 = new UTXOIndexULong64Compressed();
    UTXOIndexUInt32ArrayCompressed TableUInt32Array = new UTXOIndexUInt32ArrayCompressed();
    
    static readonly int CountNonOutputBits =
      COUNT_BATCHINDEX_BITS +
      COUNT_COLLISION_BITS_PER_TABLE * 3;
        

    public Blockchain(
      BitcoinGenesisBlock genesisBlock,
      List<HeaderLocation> checkpoints, 
      Network network)
    {
      Checkpoints = checkpoints;
      Network = network;
      GenesisBlock = genesisBlock;

      Chain = new Headerchain(genesisBlock.Header, checkpoints, network);

      Tables = new UTXOIndexCompressed[]{
        TableUInt32,
        TableULong64,
        TableUInt32Array};
      
      Merger = new UTXOMerger(this);
      ArchiveLoader = new UTXOArchiveLoader(this);
      NetworkLoader = new UTXONetworkLoader(this);
    }

    public async Task StartAsync()
    {
      await Chain.StartAsync();
      
      Merger.StartAsync();

      await ArchiveLoader.RunAsync();

      NetworkLoader.Start();
    }
    
    void InsertUTXOsUInt32(KeyValuePair<byte[], uint>[] uTXOsUInt32)
    {
      int i = 0;

    LoopUTXOItems:
      while(i < uTXOsUInt32.Length)
      {
        int primaryKey = BitConverter.ToInt32(uTXOsUInt32[i].Key, 0);
        
        for (int c = 0; c < Tables.Length; c += 1)
        {
          if (Tables[c].PrimaryTableContainsKey(primaryKey))
          {
            Tables[c].IncrementCollisionBits(primaryKey, 0);

            TableUInt32.CollisionTables[uTXOsUInt32[i].Key[0]].Add(uTXOsUInt32[i].Key, uTXOsUInt32[i].Value);

            i += 1;
            goto LoopUTXOItems;
          }
        }

        TableUInt32.PrimaryTables[(byte)primaryKey].Add(primaryKey, uTXOsUInt32[i].Value);

        i += 1;
      }
    }
    void InsertUTXOsULong64(KeyValuePair<byte[], ulong>[] uTXOsULong64)
    {
      int i = 0;

    LoopUTXOItems:
      while (i < uTXOsULong64.Length)
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
    void InsertUTXOsUInt32Array(KeyValuePair<byte[], uint[]>[] uTXOsUInt32Array)
    {
      int i = 0;

    LoopUTXOItems:
      while (i < uTXOsUInt32Array.Length)
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
    void SpendUTXOs(List<TXInput> inputs)
    {
      int i = 0;
    LoopSpendUTXOs:
      while (i < inputs.Count)
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