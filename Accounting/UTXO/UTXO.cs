using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Threading.Tasks.Dataflow;

using BToken.Chaining;
using BToken.Networking;

namespace BToken.Accounting
{
  public partial class UTXO
  {
    Headerchain Headerchain;
    Network Network;

    UTXOBuilder Builder;
    UTXOMerger Merger;
    UTXOParser Parser;

    protected static string RootPath = "UTXO";

    const int HASH_BYTE_SIZE = 32;

    const int COUNT_BATCHINDEX_BITS = 16;
    const int COUNT_HEADER_BITS = 4;
    const int COUNT_COLLISION_BITS_PER_TABLE = 2;
    const int COUNT_COLLISIONS_MAX = 3;

    UTXOTable[] Tables = new UTXOTable[]{
        new UTXOTableUInt32(),
        new UTXOTableULong64(),
        new UTXOTableUInt32Array()};

    static readonly int CountNonOutputBits =
      COUNT_BATCHINDEX_BITS +
      COUNT_HEADER_BITS +
      COUNT_COLLISION_BITS_PER_TABLE * 3;

    const int COUNT_TXS_IN_BATCH_FILE = 100;
    
    Dictionary<int, List<Block>> QueueMergeBlock = new Dictionary<int, List<Block>>();
    
    readonly object LOCK_BatchIndexMerge = new object();
    int BatchIndexNextMerger;
    Queue<Block> FIFOBlocks = new Queue<Block>();
    readonly object LOCK_FIFOIndex = new object();
    int FIFOIndex;
    byte[] HeaderHashBatchedLast = new byte[HASH_BYTE_SIZE];

    int BlockHeight;

    BitcoinGenesisBlock GenesisBlock;
    Headerchain.ChainHeader ChainHeaderMergedLast;
    Headerchain.ChainHeader HeaderLastSentToMerger;


    public UTXO(
      BitcoinGenesisBlock genesisBlock, 
      Headerchain headerchain, 
      Network network)
    {
      GenesisBlock = genesisBlock;
      Headerchain = headerchain;
      Network = network;

      Builder = new UTXOBuilder(this);
      Merger = new UTXOMerger(this);
    }

    public async Task StartAsync()
    {
      LoadUTXOState();

      Task startMergerTask = Merger.StartAsync();

      await Builder.RunAsync();
    }

    void LoadUTXOState()
    {
      try
      {
        byte[] uTXOState = File.ReadAllBytes(Path.Combine(RootPath, "UTXOState"));

        BatchIndexNextMerger = BitConverter.ToInt32(uTXOState, 0);
        BlockHeight = BitConverter.ToInt32(uTXOState, 4);
        Array.Copy(uTXOState, 8, HeaderHashBatchedLast, 0, HASH_BYTE_SIZE);

        for(int i = 0; i < Tables.Length; i += 1)
        {
          Tables[i].Load();
        }
      }
      catch
      {
        for (int c = 0; c < Tables.Length; c += 1)
        {
          Tables[c].Clear();
        }

        BatchIndexNextMerger = 0;
        BlockHeight = 0;

        InsertGenesisBlock();

        ChainHeaderMergedLast = Headerchain.GenesisHeader;
      }
    }
    void InsertGenesisBlock()
    {
      UTXOBatch genesisBatch = new UTXOBatch()
      {
        BatchIndex = 0,
        Buffer = GenesisBlock.BlockBytes
      };

      ParseBatch(genesisBatch);

      InsertUTXOs(genesisBatch);
    }

    void InsertUTXOs(UTXOBatch batch)
    {
      foreach(Block block in batch.Blocks)
      {
        for (int c = 0; c < Tables.Length; c += 1)
        {
        LoopUTXOItems:
          while (block.TryPopUTXOItem(c, out UTXOItem uTXOItem))
          {
            for (int cc = 0; cc < Tables.Length; cc += 1)
            {
              if (Tables[cc].PrimaryTableContainsKey(uTXOItem.PrimaryKey))
              {
                Tables[cc].IncrementCollisionBits(uTXOItem.PrimaryKey, c);

                Tables[c].SecondaryTableAddUTXO(uTXOItem);
                goto LoopUTXOItems;
              }
            }
            Tables[c].PrimaryTableAddUTXO(uTXOItem);
          }
        }
      }
    }
    void SpendUTXOs(UTXOBatch batch)
    {
      foreach (Block block in batch.Blocks)
      {
        for(int t = 0; t < block.TXCount; t += 1)
        {
          int i = 0;
        LoopSpendUTXOs:
          while (i < block.InputsPerTX[t].Length)
          {
            TXInput input = block.InputsPerTX[t][i];
                        
            for (int c = 0; c < Tables.Length; c += 1)
            {
              UTXOTable tablePrimary = Tables[c];

              if (tablePrimary.TryGetValueInPrimaryTable(input.PrimaryKeyTXIDOutput))
              {
                UTXOTable tableCollision = null;
                for (int cc = 0; cc < Tables.Length; cc += 1)
                {
                  if (tablePrimary.HasCollision(cc))
                  {
                    tableCollision = Tables[cc];

                    if (tableCollision.TrySpendCollision(input, tablePrimary))
                    {
                      i += 1;
                      goto LoopSpendUTXOs;
                    }
                  }
                }

                tablePrimary.SpendPrimaryUTXO(input, out bool allOutputsSpent);

                if (allOutputsSpent)
                {
                  tablePrimary.RemovePrimary();

                  if (tableCollision != null)
                  {
                    batch.StopwatchResolver.Start();
                    tableCollision.ResolveCollision(tablePrimary);
                    batch.StopwatchResolver.Stop();
                  }
                }

                i += 1;
                goto LoopSpendUTXOs;
              }
            }

            throw new UTXOException(string.Format(
              "Referenced TX {0} not found in UTXO table.",
              input.TXIDOutput.ToHexString()));
          }
        }
      }
    }
    
    static async Task WriteFileAsync(string filePath, byte[] buffer)
    {
      try
      {
        string filePathTemp = filePath + "_temp";

        using (FileStream stream = new FileStream(
           filePathTemp,
           FileMode.Create,
           FileAccess.ReadWrite,
           FileShare.Read,
           bufferSize: 4096,
           useAsync: true))
        {
          await stream.WriteAsync(buffer, 0, buffer.Length).ConfigureAwait(false);
        }

        if (File.Exists(filePath))
        {
          File.Delete(filePath);
        }
        File.Move(filePathTemp, filePath);
      }
      catch(Exception ex)
      {
        Console.WriteLine(ex.Message);
      }
    }
  }
}