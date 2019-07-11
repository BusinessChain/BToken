using System;
using System.Collections.Generic;
using System.Diagnostics;
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
        const int UTXOSTATE_ARCHIVING_INTERVAL = 50;

        UTXO UTXO;
        UTXOBuilder Builder;

        public int BlockHeight;
        public int BatchIndexNext;
        public Headerchain.ChainHeader HeaderMergedLast;
        public byte[] HeaderHashMergedLast = new byte[HASH_BYTE_SIZE];
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
                  foreach (UTXOParserData uTXOParserData in batch.UTXOParserDatasets)
                  {
                    UTXO.InsertUTXOs(uTXOParserData);
                    UTXO.SpendUTXOs(uTXOParserData);
                  }
                  StopwatchMerging.Stop();

                  BlockHeight += batch.UTXOParserDatasets.Count;
                  BatchIndexNext += 1;
                  HeaderMergedLast = batch.HeaderLast;
                  
                  if (batch.BatchIndex % UTXOSTATE_ARCHIVING_INTERVAL == 0 
                    && batch.BatchIndex > 0)
                  {
                    //await ArchiveUTXOState(); // Make Temp folder first
                  }

                  LogCSV(batch, logFileWriter);

                  if (batch.IsCancellationBatch)
                  {
                    Builder.CancellationBuilder.Cancel();
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
          catch(Exception ex)
          {
            Console.WriteLine(ex.Message);
          }
        }

        async Task ArchiveUTXOState()
        {
          Directory.CreateDirectory(RootPath);

          byte[] uTXOState = new byte[40];
          BitConverter.GetBytes(BatchIndexNext).CopyTo(uTXOState, 0);
          BitConverter.GetBytes(BlockHeight).CopyTo(uTXOState, 4);
          HeaderMergedLast.GetHeaderHash().CopyTo(uTXOState, 8);

          await WriteFileAsync(
            Path.Combine(RootPath, "UTXOState"),
            uTXOState);

          Parallel.ForEach(UTXO.Tables, c => c.BackupToDisk());
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
