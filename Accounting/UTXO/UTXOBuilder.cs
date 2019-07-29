using System;
using System.Linq;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Threading.Tasks.Dataflow;
using System.Security.Cryptography;


using BToken.Chaining;

namespace BToken.Accounting
{
  public partial class UTXO
  {
    partial class UTXOBuilder
    {
      const int COUNT_ARCHIVE_PARSER_PARALLEL = 6;

      UTXO UTXO;
      UTXOMerger Merger;
      UTXONetworkLoader NetworkLoader;
      BitcoinGenesisBlock GenesisBlock;
      
      CancellationTokenSource CancellationBuilder 
        = new CancellationTokenSource();

      readonly object LOCK_BatchIndexLoad = new object();
      int BatchIndexLoad;
      public SHA256 SHA256 = SHA256.Create();
                  
      readonly object LOCK_HeaderSentToMergerLast = new object();
      Headerchain.ChainHeader HeaderSentToMergerLast;
      int BatchIndexSentToMergerLast;
        

      public UTXOBuilder(
        UTXO uTXO,
        BitcoinGenesisBlock genesisBlock)
      {
        UTXO = uTXO;
        GenesisBlock = genesisBlock;

        Merger = new UTXOMerger(uTXO, this);
      }

      public async Task RunAsync()
      {
        LoadUTXOState();

        Task mergerTask = Merger.StartAsync();

        await RunArchiveLoaderAsync();
        
        if(HeaderSentToMergerLast.HeadersNext != null)
        {
          NetworkLoader = new UTXONetworkLoader(
            this, 
            CancellationBuilder.Token);

          NetworkLoader.Start(
            HeaderSentToMergerLast.HeadersNext[0],
            BatchIndexLoad);
        }

        await DelayUntilMergerCancelsBuilderAsync();

        Console.WriteLine("Build completed");
      }
      async Task DelayUntilMergerCancelsBuilderAsync()
      {
        try
        {
          await Task.Delay(-1, CancellationBuilder.Token).ConfigureAwait(false);
        }
        catch (TaskCanceledException)
        {
          return;
        }
      }
      
      void LoadUTXOState()
      {
        try
        {
          if(Directory.Exists(PathUTXOState))
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

          Merger.BatchIndexNext = BitConverter.ToInt32(uTXOState, 0);
          Merger.BlockHeight = BitConverter.ToInt32(uTXOState, 4);
          byte[] headerHashMergedLast = new byte[HASH_BYTE_SIZE];
          Array.Copy(uTXOState, 8, headerHashMergedLast, 0, HASH_BYTE_SIZE);

          Merger.HeaderMergedLast = UTXO.Headerchain.ReadHeader(
            headerHashMergedLast, SHA256);
          HeaderSentToMergerLast = Merger.HeaderMergedLast;

          BatchIndexLoad = Merger.BatchIndexNext;

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
          
          Merger.BatchIndexNext = 0;
          Merger.BlockHeight = -1;

          var parser = new UTXOParser(UTXO);

          UTXOBatch genesisBatch = parser.ParseBatch(
            GenesisBlock.BlockBytes, 0);

          HeaderSentToMergerLast = genesisBatch.HeaderLast;

          BatchIndexLoad = 1;

          Merger.Buffer.Post(genesisBatch);
        }
        
        BatchIndexSentToMergerLast = Merger.BatchIndexNext - 1;
      }

      async Task RunArchiveLoaderAsync()
      {
        Task[] archiveLoaderTasks = new Task[COUNT_ARCHIVE_PARSER_PARALLEL];
        for (int i = 0; i < COUNT_ARCHIVE_PARSER_PARALLEL; i += 1)
        {
          archiveLoaderTasks[i] = LoadBatchesFromArchiveAsync();
        }
        await Task.WhenAll(archiveLoaderTasks);
      }
      async Task LoadBatchesFromArchiveAsync()
      {
        UTXOParser parser = new UTXOParser(UTXO);

        byte[] batchBuffer;
        int batchIndex;

        try
        {
          while (true)
          {
            lock (LOCK_BatchIndexLoad)
            {
              batchIndex = BatchIndexLoad;
              BatchIndexLoad += 1;
            }

            try
            {
              batchBuffer = await BlockArchiver
              .ReadBlockBatchAsync(batchIndex, CancellationBuilder.Token).ConfigureAwait(false);
            }
            catch(IOException)
            {
              lock (LOCK_BatchIndexLoad)
              {
                BatchIndexLoad -= 1;
              }

              return;
            }

            UTXOBatch batch = parser.ParseBatch(batchBuffer, batchIndex);

            lock (LOCK_HeaderSentToMergerLast)
            {
              Merger.Buffer.Post(batch);

              if(batch.BatchIndex > BatchIndexSentToMergerLast)
              {
                HeaderSentToMergerLast = batch.HeaderLast;
                BatchIndexSentToMergerLast = batch.BatchIndex;
              }
            }
          }
        }
        catch (Exception ex)
        {
          Console.WriteLine(
            string.Format("Exception in archive loader with parser {0}: " + ex.Message, parser.GetHashCode()));
          throw ex;
        }
      }
    }
  }
}
