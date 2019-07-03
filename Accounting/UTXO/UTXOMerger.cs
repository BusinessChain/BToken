using System;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using System.Threading.Tasks;
using System.Threading.Tasks.Dataflow;

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
        public byte[] HeaderHashMergedLast = new byte[HASH_BYTE_SIZE];
        public BufferBlock<UTXOBatch> BatchBuffer = new BufferBlock<UTXOBatch>();
        Dictionary<int, UTXOBatch> QueueMergeBatch = new Dictionary<int, UTXOBatch>();

        long UTCTimeStartMerger;
        

        public UTXOMerger(
          UTXO uTXO,
          UTXOBuilder builder)
        {
          UTXO = uTXO;
          Builder = builder;
        }

        public async Task StartAsync()
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
              UTXOBatch batch = await BatchBuffer
                .ReceiveAsync(Builder.CancellationBuilder.Token).ConfigureAwait(false);

              if (batch.BatchIndex != BatchIndexNext)
              {
                QueueMergeBatch.Add(batch.BatchIndex, batch);
                continue;
              }

              while (true)
              {
                //if (!HeaderHashMergedLast.IsEqual(batch.Blocks.First().HeaderHashPrevious))
                //{
                //  throw new UTXOException(string.Format(
                //    "In Batch {0} previous hash {1} is not equal to last merged hash {2}",
                //    batch.BatchIndex,
                //    batch.HeaderHashPrevious.ToHexString(),
                //    HeaderHashMergedLast.ToHexString()));
                //}

                batch.StopwatchMerging.Start();
                foreach (UTXOParserData parserData in batch.UTXOParserData)
                {
                  UTXO.InsertUTXOs(parserData);
                }
                foreach (UTXOParserData parserData in batch.UTXOParserData)
                {
                  UTXO.SpendUTXOs(parserData);
                }
                batch.StopwatchMerging.Stop();

                BatchIndexNext += 1;
                BlockHeight += batch.Blocks.Count;

                if (batch.BatchIndex % UTXOSTATE_ARCHIVING_INTERVAL == 0 && batch.BatchIndex > 0)
                {
                  await ArchiveUTXOState(batch); // Make Temp folder first
                }

                LogCSV(batch, logFileWriter);

                if(batch.IsCancellationBatch)
                {
                  Builder.CancellationBuilder.Cancel();
                  break;
                }

                if (!QueueMergeBatch.TryGetValue(BatchIndexNext, out batch))
                {
                  break;
                }
              }
            }
          }
        }

        async Task ArchiveUTXOState(UTXOBatch batch)
        {
          Directory.CreateDirectory(RootPath);

          byte[] uTXOState = new byte[40];
          BitConverter.GetBytes(BatchIndexNext).CopyTo(uTXOState, 0);
          BitConverter.GetBytes(BlockHeight).CopyTo(uTXOState, 4);
          HeaderHashMergedLast.CopyTo(uTXOState, 8);

          await WriteFileAsync(
            Path.Combine(RootPath, "UTXOState"),
            uTXOState);

          Parallel.ForEach(UTXO.Tables, c => c.BackupToDisk());
        }

        void LogCSV(UTXOBatch batch, StreamWriter logFileWriter)
        {
          long timeParsing = batch.StopwatchParse.ElapsedMilliseconds;

          int ratioMergeToParse =
            (int)((float)batch.StopwatchMerging.ElapsedTicks * 100
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
