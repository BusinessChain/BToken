using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using System.Linq;



namespace BToken.Chaining
{
  public partial class UTXOTable
  {
    const int HASH_BYTE_SIZE = 32;
    const int COUNT_BATCHINDEX_BITS = 16;
    const int COUNT_COLLISION_BITS_PER_TABLE = 2;
    const int COUNT_COLLISIONS_MAX = 2 ^ COUNT_COLLISION_BITS_PER_TABLE - 1;

    const int LENGTH_BITS_UINT = 32;
    const int LENGTH_BITS_ULONG = 64;

    const int COUNT_NON_OUTPUT_BITS =
      COUNT_BATCHINDEX_BITS + 
      COUNT_COLLISION_BITS_PER_TABLE * 3;

    UTXOIndexCompressed[] Tables;
    UTXOIndexUInt32Compressed TableUInt32 = new UTXOIndexUInt32Compressed();
    UTXOIndexULong64Compressed TableULong64 = new UTXOIndexULong64Compressed();
    UTXOIndexUInt32ArrayCompressed TableUInt32Array = new UTXOIndexUInt32ArrayCompressed();
    
    long UTCTimeStartMerger;

    StreamWriter LogFile;



    public UTXOTable(byte[] genesisBlockBytes)
    {
      LogFile = new StreamWriter("logUTXOTable", false);

      Tables = new UTXOIndexCompressed[]{
        TableUInt32,
        TableULong64,
        TableUInt32Array };
    }
             

    public void LoadImage(string pathImageRoot)
    {
      string pathUTXOImage = Path.Combine(
        pathImageRoot, 
        "UTXOImage");

      for (int c = 0; c < Tables.Length; c += 1)
      {
        Tables[c].Load(pathUTXOImage);
      }

      Console.WriteLine(
        "Load UTXO Image from {0}",
        pathUTXOImage);
    }

    public void Clear()
    {
      for (int c = 0; c < Tables.Length; c += 1)
      {
        Tables[c].Clear();
      }
    }


    public void InsertBlock(
      BlockParser blockParser, 
      int indexArchive, 
      int height)
    {
      blockParser.StopwatchInsertion.Restart();

      InsertUTXOsUInt32(
        blockParser.TableUInt32,
        indexArchive);

      InsertUTXOsULong64(
        blockParser.TableULong64,
        indexArchive);

      InsertUTXOsUInt32Array(
        blockParser.TableUInt32Array,
        indexArchive);

      InsertSpendUTXOs(blockParser.Inputs);

      blockParser.StopwatchInsertion.Stop();

      LogInsertion(
        blockParser, 
        indexArchive, 
        height);
    }

    void InsertUTXO(
      byte[] uTXOKey,
      UTXOIndexCompressed table)
    {
      int primaryKey = BitConverter.ToInt32(uTXOKey, 0);

      for (int c = 0; c < Tables.Length; c += 1)
      {
        if (Tables[c].PrimaryTableContainsKey(primaryKey))
        {
          Tables[c].IncrementCollisionBits(
            primaryKey,
            table.Address);

          table.AddUTXOAsCollision(uTXOKey);

          return;
        }
      }

      table.AddUTXOAsPrimary(primaryKey);
    }

    void InsertUTXOsUInt32(
      UTXOIndexUInt32 tableUInt32,
      int indexArchive)
    {
      KeyValuePair<byte[], uint>[] uTXOsUInt32 = 
        tableUInt32.Table.ToArray();

      int i = 0;

      while (i < uTXOsUInt32.Length)
      {
        TableUInt32.UTXO =
          uTXOsUInt32[i].Value |
          ((uint)indexArchive & UTXOIndexUInt32.MaskBatchIndex);
        
        InsertUTXO(
          uTXOsUInt32[i].Key,
          TableUInt32);

        i += 1;
      }
    }

    void InsertUTXOsULong64(
      UTXOIndexULong64 tableULong64,
      int indexArchive)
    {
      KeyValuePair<byte[], ulong>[] uTXOsULong64 =
        tableULong64.Table.ToArray();

      int i = 0;

      while (i < uTXOsULong64.Length)
      {
        TableULong64.UTXO =
          uTXOsULong64[i].Value |
          ((ulong)indexArchive & UTXOIndexULong64.MaskBatchIndex);

        InsertUTXO(
          uTXOsULong64[i].Key,
          TableULong64);

        i += 1;
      }
    }

    void InsertUTXOsUInt32Array(
      UTXOIndexUInt32Array tableUInt32Array,
      int indexArchive)
    {
      KeyValuePair<byte[], uint[]>[] uTXOsUInt32Array =
        tableUInt32Array.Table.ToArray();

      int i = 0;

      while (i < uTXOsUInt32Array.Length)
      {
        TableUInt32Array.UTXO = uTXOsUInt32Array[i].Value;
        TableUInt32Array.UTXO[0] |=
          (uint)indexArchive & UTXOIndexUInt32Array.MaskBatchIndex;

        InsertUTXO(
          uTXOsUInt32Array[i].Key,
          TableUInt32Array);

        i += 1;
      }
    }

    void InsertSpendUTXOs(List<TXInput> inputs)
    {
      int i = 0;

    LoopSpendUTXOs:

      while (i < inputs.Count)
      {
        for (int c = 0; c < Tables.Length; c += 1)
        {
          UTXOIndexCompressed tablePrimary = Tables[c];

          if (tablePrimary.TryGetValueInPrimaryTable(
            inputs[i].PrimaryKeyTXIDOutput))
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

            tablePrimary.SpendPrimaryUTXO(
              inputs[i],
              out bool allOutputsSpent);

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

        throw new ChainException(
          string.Format(
            "Referenced TX {0} not found in UTXO table.",
            inputs[i].TXIDOutput.ToHexString()));
      }
    }
    
    public void CreateImage(string path)
    {
      string pathUTXOImage = Path.Combine(path, "UTXOImage");
      DirectoryInfo directoryUTXOImage = 
        new DirectoryInfo(pathUTXOImage);

      Parallel.ForEach(Tables, t =>
      {
        t.BackupImage(directoryUTXOImage.FullName);
      });
    }
    
    void LogInsertion(
      BlockParser blockParser, 
      int indexArchive, 
      int height)
    {
      if (UTCTimeStartMerger == 0)
      {
        UTCTimeStartMerger = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
      }

      int ratioMergeToParse =
        (int)((float)blockParser.StopwatchInsertion.ElapsedTicks * 100
        / blockParser.StopwatchParse.ElapsedTicks);


      string logCSV = string.Format(
        "Insertion UTXO Table: {0},{1},{2},{3},{4},{5},{6},{7},{8}",
        blockParser.Index,
        indexArchive,
        height + blockParser.Height,
        DateTimeOffset.UtcNow.ToUnixTimeSeconds() - UTCTimeStartMerger,
        ratioMergeToParse,
        blockParser.Inputs.Count,
        Tables[0].GetMetricsCSV(),
        Tables[1].GetMetricsCSV(),
        Tables[2].GetMetricsCSV());

      logCSV.Log(LogFile);
    }
  }
}
