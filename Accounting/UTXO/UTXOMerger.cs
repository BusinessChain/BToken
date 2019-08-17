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
      const int UTXOSTATE_ARCHIVING_INTERVAL = 500;

      UTXO UTXO;

      public int BlockHeight;
      public int BatchIndexNext;
      int BatchIndexMergedLast;
      public Headerchain.ChainHeader HeaderMergedLast;
      public BufferBlock<UTXOBatch> Buffer = new BufferBlock<UTXOBatch>(
        new DataflowBlockOptions { BoundedCapacity = 10});
      
      long UTCTimeStartMerger;
      public Stopwatch StopwatchMerging = new Stopwatch();
      public Stopwatch StopwatchMergingOutputs = new Stopwatch();
      public Stopwatch StopwatchMergingInputs = new Stopwatch();


      public UTXOMerger(UTXO uTXO)
      {
        UTXO = uTXO;
      }
      
      public async Task StartAsync()
      {
        LoadState();

        UTXOBatch batch;

        UTCTimeStartMerger = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

        try
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

            LogCSV(batch);
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
        BitConverter.GetBytes(BatchIndexMergedLast).CopyTo(uTXOState, 0);
        BitConverter.GetBytes(BlockHeight).CopyTo(uTXOState, 4);
        HeaderMergedLast.GetHeaderHash().CopyTo(uTXOState, 8);

        using (FileStream stream = new FileStream(
           Path.Combine(PathUTXOState, "UTXOState"),
           FileMode.Create,
           FileAccess.ReadWrite,
           FileShare.Read))
        {
          stream.Write(uTXOState, 0, uTXOState.Length);
        }

        Parallel.ForEach(UTXO.Tables, t =>
        {
          t.BackupToDisk(PathUTXOState);
        });
      }

      void LoadState()
      {
        if (Directory.Exists(PathUTXOState))
        {
          if (!TryLoadUTXOState())
          {
            Directory.Delete(PathUTXOState, true);

            if (Directory.Exists(PathUTXOStateOld))
            {
              Directory.Move(PathUTXOStateOld, PathUTXOState);

              if (TryLoadUTXOState())
              {
                return;
              }

              Directory.Delete(PathUTXOState, true);
            }
          }
        }
      }
      bool TryLoadUTXOState()
      {
        try
        {
          byte[] uTXOState = File.ReadAllBytes(Path.Combine(PathUTXOState, "UTXOState"));

          BatchIndexMergedLast = BitConverter.ToInt32(uTXOState, 0);
          BatchIndexNext = BatchIndexMergedLast + 1;
          BlockHeight = BitConverter.ToInt32(uTXOState, 4);

          byte[] headerHashMergedLast = new byte[HASH_BYTE_SIZE];
          Array.Copy(uTXOState, 8, headerHashMergedLast, 0, HASH_BYTE_SIZE);
          HeaderMergedLast = UTXO.Headerchain.ReadHeader(headerHashMergedLast);

          Parallel.ForEach(UTXO.Tables, t => t.Load());

          return true;
        }
        catch (Exception ex)
        {
          for (int c = 0; c < UTXO.Tables.Length; c += 1)
          {
            UTXO.Tables[c].Clear();
          }

          BatchIndexNext = 0;
          BlockHeight = -1;
          HeaderMergedLast = null;

          Console.WriteLine("Exception when loading UTXO state {0}", ex.Message);
          return false;
        }
      }

      void LogCSV(UTXOBatch batch)
      {
        int ratioMergeToParse =
          (int)((float)StopwatchMerging.ElapsedTicks * 100
          / batch.StopwatchParse.ElapsedTicks);

        string logCSV = string.Format(
          "{0},{1},{2},{3},{4},{5},{6},{7}",
          batch.BatchIndex,
          BlockHeight,
          DateTimeOffset.UtcNow.ToUnixTimeSeconds() - UTCTimeStartMerger,
          batch.StopwatchParse.ElapsedMilliseconds,
          ratioMergeToParse,
          UTXO.Tables[0].GetMetricsCSV(),
          UTXO.Tables[1].GetMetricsCSV(),
          UTXO.Tables[2].GetMetricsCSV());

        Console.WriteLine(logCSV);
      }
    }
  }
}
