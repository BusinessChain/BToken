using System;
using System.Collections.Generic;
using System.IO;
using System.Diagnostics;
using System.Threading.Tasks;
using System.Threading.Tasks.Dataflow;
using System.Linq;

using BToken.Networking;



namespace BToken.Chaining
{
  partial class UTXOTable : IDatabase
  {
    Headerchain Headerchain;

    byte[] GenesisBlockBytes;

    const int COUNT_TXS_IN_BATCH_FILE = 50000;
    const int HASH_BYTE_SIZE = 32;

    const int COUNT_BATCHINDEX_BITS = 16;
    const int COUNT_COLLISION_BITS_PER_TABLE = 2;
    const int COUNT_COLLISIONS_MAX = 2 ^ COUNT_COLLISION_BITS_PER_TABLE - 1;

    const int LENGTH_BITS_UINT = 32;
    const int LENGTH_BITS_ULONG = 64;

    public static readonly int CountNonOutputBits =
      COUNT_BATCHINDEX_BITS +
      COUNT_COLLISION_BITS_PER_TABLE * 3;

    UTXOIndexCompressed[] Tables;
    UTXOIndexUInt32Compressed TableUInt32 = new UTXOIndexUInt32Compressed();
    UTXOIndexULong64Compressed TableULong64 = new UTXOIndexULong64Compressed();
    UTXOIndexUInt32ArrayCompressed TableUInt32Array = new UTXOIndexUInt32ArrayCompressed();

    const int UTXOSTATE_ARCHIVING_INTERVAL = 100;
    static string PathUTXOState = "UTXOArchive";
    static string PathUTXOStateOld = PathUTXOState + "_Old";

    public int BlockHeight;
    int ArchiveIndexNext;
    Header HeaderMergedLast;

    long UTCTimeStartMerger;
    Stopwatch StopwatchMerging = new Stopwatch();

    GatewayUTXO Gateway;


    public UTXOTable(
      byte[] genesisBlockBytes,
      Headerchain headerchain,
      Network network)
    {
      Headerchain = headerchain;

      Tables = new UTXOIndexCompressed[]{
          TableUInt32,
          TableULong64,
          TableUInt32Array };

      GenesisBlockBytes = genesisBlockBytes;

      Gateway = new GatewayUTXO(
        network,
        this);
    }



    public async Task Start()
    {
      await Gateway.Start();
    }



    void InsertUTXOsUInt32(KeyValuePair<byte[], uint>[] uTXOsUInt32)
    {
      int i = 0;

      while (i < uTXOsUInt32.Length)
      {
        InsertUTXOUInt32(
          uTXOsUInt32[i].Key,
          uTXOsUInt32[i].Value);

        i += 1;
      }
    }

    void InsertUTXOsUInt32(
      KeyValuePair<byte[], uint>[] uTXOsUInt32,
      int archiveIndex)
    {
      int i = 0;

      while (i < uTXOsUInt32.Length)
      {
        uint uTXO =
          uTXOsUInt32[i].Value |
          ((uint)archiveIndex & UTXOIndexUInt32.MaskBatchIndex);

        InsertUTXOUInt32(
          uTXOsUInt32[i].Key,
          uTXO);

        i += 1;
      }
    }

    void InsertUTXOUInt32(byte[] uTXOKey, uint uTXO)
    {
      int primaryKey = BitConverter.ToInt32(uTXOKey, 0);

      for (int c = 0; c < Tables.Length; c += 1)
      {
        if (Tables[c].PrimaryTableContainsKey(primaryKey))
        {
          Tables[c].IncrementCollisionBits(primaryKey, 0);

          TableUInt32.CollisionTables[uTXOKey[0]]
            .Add(
              uTXOKey,
              uTXO);

          return;
        }
      }

      TableUInt32.PrimaryTables[(byte)primaryKey]
        .Add(
          primaryKey,
          uTXO);
    }



    void InsertUTXOsULong64(KeyValuePair<byte[], ulong>[] uTXOsULong64)
    {
      int i = 0;

      while (i < uTXOsULong64.Length)
      {
        InsertUTXOULong64(
          uTXOsULong64[i].Key,
          uTXOsULong64[i].Value);

        i += 1;
      }
    }

    void InsertUTXOULong64(byte[] uTXOKey, ulong uTXO)
    {
      int primaryKey = BitConverter.ToInt32(uTXOKey, 0);

      for (int c = 0; c < Tables.Length; c += 1)
      {
        if (Tables[c].PrimaryTableContainsKey(primaryKey))
        {
          Tables[c].IncrementCollisionBits(primaryKey, 1);

          TableULong64.CollisionTable.Add(uTXOKey, uTXO);

          return;
        }
      }

      TableULong64.PrimaryTable.Add(primaryKey, uTXO);
    }

    void InsertUTXOsULong64(
      KeyValuePair<byte[], ulong>[] uTXOsULong64,
      int archiveIndex)
    {
      int i = 0;

      while (i < uTXOsULong64.Length)
      {
        ulong uTXO =
          uTXOsULong64[i].Value |
          ((ulong)archiveIndex & UTXOIndexULong64.MaskBatchIndex);

        InsertUTXOULong64(
          uTXOsULong64[i].Key,
          uTXO);

        i += 1;
      }
    }



    void InsertUTXOsUInt32Array(KeyValuePair<byte[], uint[]>[] uTXOsUInt32Array)
    {
      int i = 0;

      while (i < uTXOsUInt32Array.Length)
      {
        InsertUTXOUInt32Array(
          uTXOsUInt32Array[i].Key,
          uTXOsUInt32Array[i].Value);

        i += 1;
      }
    }

    void InsertUTXOUInt32Array(byte[] uTXOKey, uint[] uTXO)
    {
      int primaryKey = BitConverter.ToInt32(uTXOKey, 0);

      for (int c = 0; c < Tables.Length; c += 1)
      {
        if (Tables[c].PrimaryTableContainsKey(primaryKey))
        {
          Tables[c].IncrementCollisionBits(primaryKey, 2);

          TableUInt32Array.CollisionTable.Add(uTXOKey, uTXO);

          return;
        }
      }

      TableUInt32Array.PrimaryTable.Add(primaryKey, uTXO);
    }

    void InsertUTXOsUInt32Array(
      KeyValuePair<byte[], uint[]>[] uTXOsUInt32Array,
      int archiveIndex)
    {
      int i = 0;

      while (i < uTXOsUInt32Array.Length)
      {
        uint[] uTXO = uTXOsUInt32Array[i].Value;
        uTXO[0] |= (uint)archiveIndex & UTXOIndexUInt32Array.MaskBatchIndex;

        InsertUTXOUInt32Array(
          uTXOsUInt32Array[i].Key,
          uTXO);

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



    void ArchiveState()
    {
      if (Directory.Exists(PathUTXOState))
      {
        if (Directory.Exists(PathUTXOStateOld))
        {
          Directory.Delete(PathUTXOStateOld, true);
        }
        Directory.Move(PathUTXOState, PathUTXOStateOld);
      }

      Directory.CreateDirectory(PathUTXOState);

      byte[] uTXOState = new byte[40];
      BitConverter.GetBytes(ArchiveIndexNext).CopyTo(uTXOState, 0);
      BitConverter.GetBytes(BlockHeight).CopyTo(uTXOState, 4);
      HeaderMergedLast.HeaderHash.CopyTo(uTXOState, 8);

      using (FileStream stream = new FileStream(
         Path.Combine(PathUTXOState, "UTXOState"),
         FileMode.Create,
         FileAccess.ReadWrite,
         FileShare.Read))
      {
        stream.Write(uTXOState, 0, uTXOState.Length);
      }

      Parallel.ForEach(Tables, t =>
      {
        t.BackupToDisk(PathUTXOState);
      });
    }



    public void LoadImage(out int archiveIndexNext)
    {
      if (TryLoadUTXOState())
      {
        archiveIndexNext = ArchiveIndexNext;
        return;
      }

      if (Directory.Exists(PathUTXOState))
      {
        Directory.Delete(PathUTXOState, true);
      }

      if (Directory.Exists(PathUTXOStateOld))
      {
        Directory.Move(PathUTXOStateOld, PathUTXOState);

        if (TryLoadUTXOState())
        {
          archiveIndexNext = ArchiveIndexNext;
          return;
        }

        Directory.Delete(PathUTXOState, true);
      }

      DataBatch genesisBatch = new DataBatch(0);

      BlockBatchContainer genesisBlockContainer = new BlockBatchContainer(
        new BlockParser(Headerchain),
        0,
        GenesisBlockBytes);

      genesisBatch.ItemBatchContainers.Add(genesisBlockContainer);

      genesisBatch.Parse();

      TryInsertBatch(genesisBatch, out ItemBatchContainer containerInvalid);

      archiveIndexNext = ArchiveIndexNext;
      return;
    }



    bool TryLoadUTXOState()
    {
      try
      {
        byte[] uTXOState = File.ReadAllBytes(Path.Combine(PathUTXOState, "UTXOState"));

        ArchiveIndexNext = BitConverter.ToInt32(uTXOState, 0);
        BlockHeight = BitConverter.ToInt32(uTXOState, 4);

        byte[] headerHashMergedLast = new byte[HASH_BYTE_SIZE];
        Array.Copy(uTXOState, 8, headerHashMergedLast, 0, HASH_BYTE_SIZE);
        HeaderMergedLast = Headerchain.ReadHeader(headerHashMergedLast);

        for (int c = 0; c < Tables.Length; c += 1)
        {
          Tables[c].Load();
        }

        return true;
      }
      catch (Exception ex)
      {
        for (int c = 0; c < Tables.Length; c += 1)
        {
          Tables[c].Clear();
        }

        ArchiveIndexNext = 0;
        BlockHeight = -1;
        HeaderMergedLast = null;

        Console.WriteLine("Exception when loading UTXO state {0}", ex.Message);
        return false;
      }
    }



    public bool TryInsertContainer(ItemBatchContainer container)
    {
      BlockBatchContainer blockContainer = (BlockBatchContainer)container;

      if (blockContainer.HeaderPrevious != HeaderMergedLast)
      {
        Console.WriteLine("HeaderPrevious {0} of batch {1} not equal to \nHeaderMergedLast {2}",
          blockContainer.HeaderPrevious.HeaderHash.ToHexString(),
          blockContainer.Index,
          HeaderMergedLast.HeaderHash.ToHexString());

        return false;
      }


      StopwatchMerging.Restart();

      try
      {
        InsertUTXOsUInt32(blockContainer.UTXOsUInt32);
        InsertUTXOsULong64(blockContainer.UTXOsULong64);
        InsertUTXOsUInt32Array(blockContainer.UTXOsUInt32Array);
        SpendUTXOs(blockContainer.Inputs);
      }
      catch (ChainException ex)
      {
        Console.WriteLine(
          "Insertion of blockBatchContainer {0} raised ChainException:\n {1}.",
          blockContainer.Index,
          ex.Message);

        return false;
      }

      StopwatchMerging.Stop();

      BlockHeight += blockContainer.BlockCount;
      HeaderMergedLast = blockContainer.Header;

      if (blockContainer.Index % UTXOSTATE_ARCHIVING_INTERVAL == 0
        && blockContainer.Index > 0)
      {
        ArchiveIndexNext += UTXOSTATE_ARCHIVING_INTERVAL;
        ArchiveState();
      }

      LogCSV(
        new List<ItemBatchContainer>() { blockContainer },
        blockContainer.Index);

      return true;
    }


    public bool TryInsertBatch(DataBatch uTXOBatch, out ItemBatchContainer containerInvalid)
    {
      BlockBatchContainer blockContainerFirst = (BlockBatchContainer)uTXOBatch.ItemBatchContainers[0];

      if (blockContainerFirst.HeaderPrevious != HeaderMergedLast)
      {
        Console.WriteLine("HeaderPrevious {0} of batch {1} not equal to \nHeaderMergedLast {2}",
          blockContainerFirst.HeaderPrevious.HeaderHash.ToHexString(),
          uTXOBatch.Index,
          HeaderMergedLast.HeaderHash.ToHexString());

        containerInvalid = blockContainerFirst;
        return false;
      }

      StopwatchMerging.Restart();

      foreach (BlockBatchContainer blockContainer in uTXOBatch.ItemBatchContainers)
      {
        try
        {
          InsertUTXOsUInt32(blockContainer.UTXOsUInt32, uTXOBatch.Index);
          InsertUTXOsULong64(blockContainer.UTXOsULong64, uTXOBatch.Index);
          InsertUTXOsUInt32Array(blockContainer.UTXOsUInt32Array, uTXOBatch.Index);
          SpendUTXOs(blockContainer.Inputs);
        }
        catch (ChainException ex)
        {
          Console.WriteLine(
            "Insertion of blockBatchContainer {0} raised ChainException:\n {1}.",
            blockContainer.Index,
            ex.Message);

          containerInvalid = blockContainer;
          return false;
        }

        BlockHeight += blockContainer.BlockCount;
        HeaderMergedLast = blockContainer.Header;
      }

      StopwatchMerging.Stop();

      if (uTXOBatch.Index % UTXOSTATE_ARCHIVING_INTERVAL == 0
        && uTXOBatch.Index > 0)
      {
        ArchiveIndexNext += UTXOSTATE_ARCHIVING_INTERVAL;
        ArchiveState();
      }

      LogCSV(
        uTXOBatch.ItemBatchContainers,
        uTXOBatch.Index);

      containerInvalid = null;
      return true;
    }



    string FilePath = "J:\\BlockArchivePartitioned";

    public async Task ArchiveBatch(DataBatch uTXOBatch)
    {
      Directory.CreateDirectory(FilePath);
      string filePath = Path.Combine(FilePath, "p" + uTXOBatch.Index);

      try
      {
        using (FileStream file = new FileStream(
          filePath,
          FileMode.Create,
          FileAccess.Write,
          FileShare.None,
          bufferSize: 1048576,
          useAsync: true))
        {
          foreach (BlockBatchContainer blockContainer in uTXOBatch.ItemBatchContainers)
          {
            await file.WriteAsync(blockContainer.Buffer, 0, blockContainer.Buffer.Length);
          }
        }
      }
      catch (Exception ex)
      {
        Console.WriteLine(ex.Message);
      }
    }


    public ItemBatchContainer LoadDataContainer(int batchIndex)
    {
      return new BlockBatchContainer(
        new BlockParser(Headerchain),
        batchIndex,
        File.ReadAllBytes(
          Path.Combine(FilePath, "p" + batchIndex)));
    }



    readonly object LOCK_HeaderLoad = new object();
    int IndexLoad;

    public bool TryLoadBatch(
      ItemBatchContainer containerInsertedLast,
      out DataBatch uTXOBatch,
      int countHeaders)
    {
      Header headerLoadedLast =
        ((BlockBatchContainer)containerInsertedLast)
        .Header;

      lock (LOCK_HeaderLoad)
      {
        if (headerLoadedLast.HeadersNext.Count == 0)
        {
          uTXOBatch = null;
          return false;
        }

        uTXOBatch = new DataBatch(IndexLoad++);

        for (int i = 0; i < countHeaders; i += 1)
        {
          headerLoadedLast = headerLoadedLast.HeadersNext[0];

          BlockBatchContainer blockContainer =
            new BlockBatchContainer(
              new BlockParser(Headerchain),
              headerLoadedLast);

          uTXOBatch.ItemBatchContainers.Add(blockContainer);

          if (headerLoadedLast.HeadersNext.Count == 0)
          {
            uTXOBatch.IsFinalBatch = true;
            break;
          }
        }

        return true;
      }
    }


    void LogCSV(List<ItemBatchContainer> batchContainers, int index)
    {
      if (UTCTimeStartMerger == 0)
      {
        UTCTimeStartMerger = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
      }

      long elapsedTicksParsing = batchContainers
        .Sum(c => c.StopwatchParse.ElapsedTicks);

      int ratioMergeToParse =
        (int)((float)StopwatchMerging.ElapsedTicks * 100
        / elapsedTicksParsing);

      string logCSV = string.Format(
        "{0},{1},{2},{3},{4},{5},{6}",
        index,
        BlockHeight,
        DateTimeOffset.UtcNow.ToUnixTimeSeconds() - UTCTimeStartMerger,
        ratioMergeToParse,
        Tables[0].GetMetricsCSV(),
        Tables[1].GetMetricsCSV(),
        Tables[2].GetMetricsCSV());

      Console.WriteLine(logCSV);
    }
  }
}
