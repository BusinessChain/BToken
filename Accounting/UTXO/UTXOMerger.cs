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
    class UTXOMerger
    {
      const int UTXOSTATE_ARCHIVING_INTERVAL = 50;

      UTXO UTXO;

      public BufferBlock<UTXOBatch> BatchBuffer = new BufferBlock<UTXOBatch>();
      Dictionary<int, UTXOBatch> QueueMergeBatch = new Dictionary<int, UTXOBatch>();
      int BatchIndexNext;
      int BlockHeight;

      long UTCTimeStartMerger;


      public UTXOMerger(
        UTXO uTXO)
      {
        UTXO = uTXO;
      }

      public async Task StartAsync(
        int batchIndexNext,
        int blockHeight)
      {
        BatchIndexNext = batchIndexNext;
        BlockHeight = blockHeight;

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
            UTXOBatch batch = await BatchBuffer.ReceiveAsync().ConfigureAwait(false);

            if (batch.BatchIndex != BatchIndexNext)
            {
              QueueMergeBatch.Add(batch.BatchIndex, batch);
              continue;
            }

            while(true)
            {
              batch.StopwatchMerging.Start();
              UTXO.InsertUTXOs(batch);
              UTXO.SpendUTXOs(batch);
              batch.StopwatchMerging.Stop();

              BatchIndexNext += 1;
              BlockHeight += batch.Blocks.Count;

              if (batch.BatchIndex % UTXOSTATE_ARCHIVING_INTERVAL == 0 && batch.BatchIndex > 0)
              {
                await ArchiveUTXOState(batch);
              }
              
              LogCSV(batch, logFileWriter);

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
        batch.Blocks.Last().HeaderHash.CopyTo(uTXOState, 8);

        await WriteFileAsync(
          Path.Combine(RootPath, "UTXOState"),
          uTXOState);

        Parallel.ForEach(UTXO.Tables, c => c.BackupToDisk());
      }

      internal Task RunAsync(object batchIndexMergeNext, int blockHeight)
      {
        throw new NotImplementedException();
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
          UTXO.BlockHeight,
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
