using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Threading.Tasks.Dataflow;
using System.Security.Cryptography;

using BToken.Chaining;
using BToken.Networking;

namespace BToken.Accounting
{
  public partial class UTXO
  {
    Headerchain Headerchain;
    Network Network;

    ArchiveUTXOBatchLoader ArchiveLoader;
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

    const int COUNT_DOWNLOAD_TASKS = 8;
    const int COUNT_TXS_IN_BATCH_FILE = 100;
    
    Dictionary<int, List<Block>> QueueMergeBlock = new Dictionary<int, List<Block>>();
    Queue<Block> FIFOBlocks = new Queue<Block>();
    int TXCountFIFO;
    readonly object LOCK_BatchIndexMerge = new object();
    int BatchIndexNextMerger;
    readonly object LOCK_FIFOIndex = new object();
    int FIFOIndex;
    readonly object LOCK_TryGetHeaderHashes = new object();
    int DownloadIndex;

    int BlockHeight;

    Headerchain.ChainHeader HeaderLastSentToMerger;
    byte[] HeaderHashLastSentToMerger = new byte[HASH_BYTE_SIZE];

    Headerchain.ChainHeader ChainHeaderMergedLast;
    BitcoinGenesisBlock GenesisBlock;


    public UTXO(
      BitcoinGenesisBlock genesisBlock, 
      Headerchain headerchain, 
      Network network)
    {
      GenesisBlock = genesisBlock;
      Headerchain = headerchain;
      Network = network;

      ArchiveLoader = new ArchiveUTXOBatchLoader(this);
      Merger = new UTXOMerger(this);
    }

    public void Start()
    {
      LoadUTXOState();

      Task runMergerTask = Merger.StartAsync(
        BatchIndexNextMerger,
        BlockHeight);
    }

    public async Task BuildAsync()
    {
      await ArchiveLoader.RunAsync(BatchIndexNextMerger);
      await NetworkLoaderRunAsync();
    }
    
    void LoadUTXOState()
    {
      try
      {
        byte[] uTXOState = File.ReadAllBytes(Path.Combine(RootPath, "UTXOState"));

        BatchIndexNextMerger = BitConverter.ToInt32(uTXOState, 0);
        BlockHeight = BitConverter.ToInt32(uTXOState, 4);

        byte[] headerHashMergedLast = new byte[HASH_BYTE_SIZE];
        Array.Copy(uTXOState, 8, headerHashMergedLast, 0, HASH_BYTE_SIZE);
        ChainHeaderMergedLast = Headerchain.ReadHeader(headerHashMergedLast);

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
    async Task NetworkLoaderRunAsync()
    {
      Task[] downloadTasks = new Task[COUNT_DOWNLOAD_TASKS];
      for (int i = 0; i < COUNT_DOWNLOAD_TASKS; i += 1)
      {
        var sessionBlockDownload = new SessionBlockDownload(this);
        downloadTasks[i] = Network.ExecuteSessionAsync(sessionBlockDownload);
      }
      await Task.WhenAll(downloadTasks);
    }
        
    bool TryGetHeaderHashes(
      out byte[][] headerHashes,
      out int downloadIndex,
      int count,
      SHA256 sHA256)
    {
      int i = 0;
      headerHashes = new byte[count][];

      lock (LOCK_TryGetHeaderHashes)
      {
        downloadIndex = DownloadIndex;
        DownloadIndex += 1;

        HeaderLastSentToMerger = Headerchain.ReadHeader(HeaderHashLastSentToMerger, sHA256);
        
        while (i < count && HeaderLastSentToMerger.HeadersNext != null)
        {
          headerHashes[i] = HeaderLastSentToMerger.GetHeaderHash(sHA256);
          HeaderLastSentToMerger = HeaderLastSentToMerger.HeadersNext[0];
          i += 1;
        }
      }

      if (i > 0)
      {
        return true;
      }
      else
      {
        return false;
      }
    }

    void MergeBlocks(List<Block> blocks, int downloadIndex)
    {
      lock (LOCK_FIFOIndex)
      {
        if (FIFOIndex != downloadIndex)
        {
          QueueMergeBlock.Add(downloadIndex, blocks);
          return;
        }
      }
      
      while(true)
      {
        foreach (Block block in blocks)
        {
          FIFOBlocks.Enqueue(block);
          TXCountFIFO += block.TXCount;
        }

        while (TXCountFIFO > COUNT_TXS_IN_BATCH_FILE)
        {
          UTXOBatch batch = new UTXOBatch()
          {
            BatchIndex = BatchIndexNextMerger
          };

          BatchIndexNextMerger += 1;

          int tXCountBatch = 0;
          while (tXCountBatch < COUNT_TXS_IN_BATCH_FILE)
          {
            Block block = FIFOBlocks.Dequeue();
            batch.Blocks.Add(block);

            tXCountBatch += block.TXCount;
            TXCountFIFO -= block.TXCount;
          }

          Task archiveBlocksTask = BlockArchiver.ArchiveBlocksAsync(
            batch.Blocks,
            batch.BatchIndex);

          Merger.BatchBuffer.Post(batch);
        }

        lock (LOCK_FIFOIndex)
        {
          FIFOIndex += 1;

          if (QueueMergeBlock.TryGetValue(FIFOIndex, out blocks))
          {
            continue;
          }
        }
      }
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