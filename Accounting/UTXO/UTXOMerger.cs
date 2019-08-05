using System;
using System.Diagnostics;
using System.Linq;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Threading.Tasks.Dataflow;

using BToken.Chaining;

namespace BToken.Accounting
{
  public partial class UTXO
  {
    class UTXOMerger
    {
      const int UTXOSTATE_ARCHIVING_INTERVAL = 1000;

      UTXO UTXO;

      public int BlockHeight;
      public int BatchIndexNext;
      public int BatchIndexMergedLast;
      public Headerchain.ChainHeader HeaderMergedLast;
      public BufferBlock<UTXOBatch> Buffer = new BufferBlock<UTXOBatch>();
      
      long UTCTimeStartMerger;
      public Stopwatch StopwatchMerging = new Stopwatch();


      public UTXOMerger(UTXO uTXO)
      {
        UTXO = uTXO;
      }

      void LoadState()
      {
        try
        {
          if (Directory.Exists(PathUTXOState))
          {
            if (Directory.Exists(PathUTXOStateTemporary))
            {
              Directory.Delete(PathUTXOStateTemporary, true);
            }
          }
          else
          {
            if (Directory.Exists(PathUTXOStateOld))
            {
              Directory.Delete(PathUTXOStateOld, true);
            }

            Directory.Move(PathUTXOStateTemporary, PathUTXOState);
          }

          byte[] uTXOState = File.ReadAllBytes(Path.Combine(PathUTXOState, "UTXOState"));

          BatchIndexMergedLast = BitConverter.ToInt32(uTXOState, 0);
          BatchIndexNext = BatchIndexMergedLast + 1;
          BlockHeight = BitConverter.ToInt32(uTXOState, 4);

          byte[] headerHashMergedLast = new byte[HASH_BYTE_SIZE];
          Array.Copy(uTXOState, 8, headerHashMergedLast, 0, HASH_BYTE_SIZE);
          HeaderMergedLast = UTXO.Headerchain.ReadHeader(headerHashMergedLast);

          for (int i = 0; i < UTXO.Tables.Length; i += 1)
          {
            UTXO.Tables[i].Load();
          }
        }
        catch
        {
          for (int c = 0; c < UTXO.Tables.Length; c += 1)
          {
            UTXO.Tables[c].Clear();
          }

          BatchIndexNext = 0;
          BlockHeight = -1;
        }
      }

      public async Task StartAsync()
      {
        LoadState();

        UTXOBatch batch;

        try
        {
          UTCTimeStartMerger = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

          DirectoryInfo directory = Directory.CreateDirectory("UTXOMerger");

          string pathLogFile = Path.Combine(
            directory.FullName,
            "UTXOMerger-" + DateTime.Now.ToString("yyyyddM-HHmmss") + ".csv");

          using (StreamWriter logFileWriter = new StreamWriter(
           new FileStream(
             pathLogFile,
             FileMode.Append,
             FileAccess.Write,
             FileShare.Read)))
          {
            while (true)
            {
              batch = await Buffer
                .ReceiveAsync().ConfigureAwait(false);
              
              StopwatchMerging.Restart();

              UTXO.InsertUTXOsUInt32(batch.UTXOsUInt32);
              UTXO.InsertUTXOsULong64(batch.UTXOsULong64);
              UTXO.InsertUTXOsUInt32Array(batch.UTXOsUInt32Array);
              UTXO.SpendUTXOs(batch.Inputs);

              StopwatchMerging.Stop();

              BlockHeight += batch.BlockCount;
              BatchIndexMergedLast = batch.BatchIndex;
              HeaderMergedLast = batch.HeaderLast;

              if (batch.BatchIndex % UTXOSTATE_ARCHIVING_INTERVAL == 0
                && batch.BatchIndex > 0)
              {
                ArchiveState();
              }

              LogCSV(batch, logFileWriter);
            }
          }
        }
        catch (Exception ex)
        {
          Console.WriteLine(ex.Message);
          throw ex;
        }
      }
      
      void ArchiveState()
      {
        Directory.CreateDirectory(PathUTXOStateTemporary);

        byte[] uTXOState = new byte[40];
        BitConverter.GetBytes(BatchIndexMergedLast).CopyTo(uTXOState, 0);
        BitConverter.GetBytes(BlockHeight).CopyTo(uTXOState, 4);
        HeaderMergedLast.GetHeaderHash().CopyTo(uTXOState, 8);

        using (FileStream stream = new FileStream(
           Path.Combine(PathUTXOStateTemporary, "UTXOState"),
           FileMode.Create,
           FileAccess.ReadWrite,
           FileShare.Read))
        {
          stream.Write(uTXOState, 0, uTXOState.Length);
        }

        BackupTables(PathUTXOStateTemporary);
      }

      void BackupTables(string path)
      {
        Parallel.ForEach(UTXO.Tables, t =>
        {
          t.BackupToDisk(PathUTXOStateTemporary);
        });

        if (Directory.Exists(PathUTXOState))
        {
          Directory.Move(PathUTXOState, PathUTXOStateOld);
          Directory.Delete(PathUTXOStateOld, true);
        }

        Directory.Move(PathUTXOStateTemporary, PathUTXOState);
      }
      async Task BackupTablesAsync(string path)
      {
        Task[] backupTasks = new Task[UTXO.Tables.Length];
        Parallel.For(0, UTXO.Tables.Length, i =>
        {
          backupTasks[i] = UTXO.Tables[i].BackupToDiskAsync(PathUTXOStateTemporary);
        });

        await Task.WhenAll(backupTasks);

        if (Directory.Exists(PathUTXOState))
        {
          Directory.Move(PathUTXOState, PathUTXOStateOld);
          Directory.Delete(PathUTXOStateOld, true);
        }

        Directory.Move(PathUTXOStateTemporary, PathUTXOState);
      }

      void LogCSV(UTXOBatch batch, StreamWriter logFileWriter)
      {
        long timeParsing = batch.StopwatchParse.ElapsedMilliseconds;

        int ratioMergeToParse =
          (int)((float)StopwatchMerging.ElapsedTicks * 100
          / batch.StopwatchParse.ElapsedTicks);

        int ratioBatchInputSpend =
          (int)((float)StopwatchMerging.ElapsedTicks * 100
          / batch.StopwatchParse.ElapsedTicks);

        string logCSV = string.Format(
          "{0},{1},{2},{3},{4},{5},{6},{7}",
          batch.BatchIndex,
          BlockHeight,
          DateTimeOffset.UtcNow.ToUnixTimeSeconds() - UTCTimeStartMerger,
          timeParsing,
          ratioMergeToParse,
          UTXO.Tables[0].GetMetricsCSV(),
          UTXO.Tables[1].GetMetricsCSV(),
          UTXO.Tables[2].GetMetricsCSV());

        Console.WriteLine(logCSV);
        logFileWriter.WriteLine(logCSV);
      }


    }
  }
}
