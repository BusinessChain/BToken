using System;
using System.Diagnostics;
using System.Linq;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using System.Threading.Tasks.Dataflow;

using BToken.Chaining;

namespace BToken.Accounting
{
  public partial class UTXO
  {
    partial class UTXOBuilder
    {
      class UTXOMerger
      {
        const int UTXOSTATE_ARCHIVING_INTERVAL = 300;

        UTXO UTXO;
        UTXOBuilder Builder;

        public int BlockHeight;
        public int BatchIndexNext;
        public Headerchain.ChainHeader HeaderMergedLast;
        public BufferBlock<UTXOBatch> Buffer = new BufferBlock<UTXOBatch>();
        Dictionary<int, UTXOBatch> QueueMergeBatch = new Dictionary<int, UTXOBatch>();

        long UTCTimeStartMerger;
        public Stopwatch StopwatchMerging = new Stopwatch();


        public UTXOMerger(
          UTXO uTXO,
          UTXOBuilder builder)
        {
          UTXO = uTXO;
          Builder = builder;
        }

        public async Task StartAsync()
        {
          try
          {
            UTCTimeStartMerger = DateTimeOffset.UtcNow.ToUnixTimeSeconds();

            string pathLogFile = Path.Combine(
              Directory.CreateDirectory("UTXOBuild").FullName,
              "UTXOBuild-" + DateTime.Now.ToString("yyyyddM-HHmmss") + ".csv");

            using (StreamWriter logFileWriter = new StreamWriter(
             new FileStream(
               pathLogFile,
               FileMode.Append,
               FileAccess.Write,
               FileShare.Read)))
            {
              while (true)
              {
                UTXOBatch batch = await Buffer
                  .ReceiveAsync(Builder.CancellationBuilder.Token).ConfigureAwait(false);
                
                if (batch.BatchIndex != BatchIndexNext)
                {
                  QueueMergeBatch.Add(batch.BatchIndex, batch);
                  continue;
                }

                while (true)
                {
                  if (HeaderMergedLast != batch.HeaderPrevious)
                  {
                    throw new UTXOException(
                      string.Format("HeaderPrevious {0} of Batch {1} not equal to \nHeaderMergedLast {2}",
                      batch.HeaderPrevious.GetHeaderHash().ToHexString(),
                      batch.BatchIndex,
                      HeaderMergedLast.GetHeaderHash().ToHexString()));
                  }

                  StopwatchMerging.Restart();
                  
                  UTXO.InsertUTXOsUInt32(batch.UTXOsUInt32);
                  UTXO.InsertUTXOsULong64(batch.UTXOsULong64);
                  UTXO.InsertUTXOsUInt32Array(batch.UTXOsUInt32Array);
                  UTXO.SpendUTXOs(batch.Inputs, batch.IndexInputs);

                  StopwatchMerging.Stop();

                  BlockHeight += batch.BlockCount;
                  BatchIndexNext += 1;
                  HeaderMergedLast = batch.HeaderLast;

                  if (batch.BatchIndex % UTXOSTATE_ARCHIVING_INTERVAL == 0
                    && batch.BatchIndex > 0)
                  {
                    //ArchiveUTXOState();
                  }

                  LogCSV(batch, logFileWriter);

                  if (batch.IsCancellationBatch)
                  {
                    Console.WriteLine("Merger cancels builder");
                    Builder.CancellationBuilder.Cancel();
                    Console.WriteLine("Last merged hash {0}", HeaderMergedLast.GetHeaderHash().ToHexString());

                    break;
                  }

                  if (QueueMergeBatch.TryGetValue(BatchIndexNext, out batch))
                  {
                    QueueMergeBatch.Remove(BatchIndexNext);
                  }
                  else
                  {
                    break;
                  }
                }
              }
            }
          }
          catch (Exception ex)
          {
            Console.WriteLine(ex.Message);
            Builder.CancellationBuilder.Cancel();
            throw ex;
          }
        }

        void ArchiveUTXOState()
        {
          Directory.CreateDirectory(PathUTXOStateTemporary);

          byte[] uTXOState = new byte[40];
          BitConverter.GetBytes(BatchIndexNext).CopyTo(uTXOState, 0);
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
}
