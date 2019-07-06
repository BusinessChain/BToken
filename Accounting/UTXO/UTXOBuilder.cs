using System;
using System.Collections.Generic;
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
      UTXO UTXO;
      UTXOMerger Merger;
      BitcoinGenesisBlock GenesisBlock;

      const int COUNT_PROCESSING_BATCHES_PARALLEL = 1;
      const int COUNT_DOWNLOAD_TASKS_PARALLEL = 8;
      const int COUNT_BLOCKS_DOWNLOAD_BATCH = 10;
      const int COUNT_TXS_IN_BATCH_FILE = 100;

      CancellationTokenSource CancellationBuilder 
        = new CancellationTokenSource();

      readonly object LOCK_BatchIndexLoad = new object();
      int BatchIndexLoad;
      readonly object LOCK_BatchIndexMerge = new object();
      int DownloadBatcherIndex;
      public SHA256 SHA256 = SHA256.Create();

      BufferBlock<UTXODownloadBatch> BatcherBuffer 
        = new BufferBlock<UTXODownloadBatch>();
      BufferBlock<UTXODownloadBatch> DownloaderBuffer
        = new BufferBlock<UTXODownloadBatch>();

      Dictionary<int, UTXODownloadBatch> QueueDownloadBatch 
        = new Dictionary<int, UTXODownloadBatch>();

      Queue<Block> FIFOBlocks = new Queue<Block>();
      int TXCountFIFO;

      BufferBlock<UTXOBatch> BatchParserBuffer = new BufferBlock<UTXOBatch>();

      readonly object LOCK_HeaderSentToMergerLast = new object();
      Headerchain.ChainHeader HeaderSentToMergerLast;


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
          StartNetworkLoader();
        }

        await DelayUntilMergerCancelsBuilderAsync();
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
          byte[] uTXOState = File.ReadAllBytes(Path.Combine(RootPath, "UTXOState"));

          Merger.BatchIndexNext = BitConverter.ToInt32(uTXOState, 0);
          Merger.BlockHeight = BitConverter.ToInt32(uTXOState, 4);
          Array.Copy(uTXOState, 8, Merger.HeaderHashMergedLast, 0, HASH_BYTE_SIZE);
          
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

          InsertGenesisBlock();

          Merger.BatchIndexNext = 1;
          Merger.BlockHeight = 0;
          Merger.HeaderHashMergedLast = UTXO.Headerchain.GenesisHeader.GetHeaderHash();
        }
        
        BatchIndexLoad = Merger.BatchIndexNext;

        HeaderSentToMergerLast = UTXO.Headerchain.ReadHeader(
          Merger.HeaderHashMergedLast, SHA256);
      }
      void InsertGenesisBlock()
      {
        var parser = new UTXOParser(UTXO);

        UTXOBatch genesisBatch = parser.ParseBatch(
          GenesisBlock.BlockBytes, 0);

        UTXO.InsertUTXOs(genesisBatch.UTXOParserDatasets.First());
      }

      async Task RunArchiveLoaderAsync()
      {
        Task[] archiveLoaderTasks = new Task[COUNT_PROCESSING_BATCHES_PARALLEL];
        for (int i = 0; i < COUNT_PROCESSING_BATCHES_PARALLEL; i += 1)
        {
          archiveLoaderTasks[i] = LoadBatchesFromArchiveAsync();
        }
        await Task.WhenAll(archiveLoaderTasks);
      }
      async Task LoadBatchesFromArchiveAsync()
      {
        UTXOParser parser = new UTXOParser(UTXO);

        try
        {
          while (true)
          {
            byte[] batchBuffer;
            int batchIndex;

            lock (LOCK_BatchIndexLoad)
            {
              batchIndex = BatchIndexLoad;
              BatchIndexLoad += 1;
            }

            try
            {
              batchBuffer = await BlockArchiver
              .ReadBlockBatchAsync(batchIndex).ConfigureAwait(false);
            }
            catch(IOException)
            {
              lock (LOCK_BatchIndexLoad)
              {
                BatchIndexLoad -= 1;
                return;
              }
            }

            UTXOBatch batch = parser.ParseBatch(batchBuffer, batchIndex);

            lock(LOCK_HeaderSentToMergerLast)
            {
              Merger.BatchBuffer.Post(batch);
              HeaderSentToMergerLast = parser.ChainHeader;
            }
          }
        }
        catch (Exception ex)
        {
          Console.WriteLine(ex.Message);
          throw ex;
        }
      }

      void StartNetworkLoader()
      {
        for (int i = 0; i < COUNT_PROCESSING_BATCHES_PARALLEL; i += 1)
        {
          Task parserTasks = RunTXParserAsync();
        }

        Task runBatcherTask = RunBatcherAsync();
        
        for (int i = 0; i < COUNT_DOWNLOAD_TASKS_PARALLEL; i += 1)
        {
          Task runDownloadTask = UTXO.Network.RunSessionAsync(
            new SessionBlockDownload(this));
        }

        CreateDownloadBatches();
      }

      async Task RunTXParserAsync()
      {
        UTXOParser parser = new UTXOParser(UTXO);
        
        while(true)
        {
          UTXOBatch batch = await BatchParserBuffer
            .ReceiveAsync(CancellationBuilder.Token).ConfigureAwait(false);

          parser.ParseBatch(batch);

          //await BlockArchiver.ArchiveBatchAsync(batch);

          Merger.BatchBuffer.Post(batch);
        }
      }
      async Task RunBatcherAsync()
      {
        UTXODownloadBatch downloadBatch;

        while (true)
        {
          try
          {
            downloadBatch = await BatcherBuffer
              .ReceiveAsync(CancellationBuilder.Token).ConfigureAwait(false);
          }
          catch (TaskCanceledException)
          {
            return;
          }

          if (downloadBatch.BatchIndex != DownloadBatcherIndex)
          {
            QueueDownloadBatch.Add(downloadBatch.BatchIndex, downloadBatch);
            continue;
          }

          do
          {
            foreach (Block block in downloadBatch.Blocks)
            {
              FIFOBlocks.Enqueue(block);
              TXCountFIFO += block.TXCount;
            }

            while (
              TXCountFIFO > COUNT_TXS_IN_BATCH_FILE ||
              (downloadBatch.IsCancellationBatch && TXCountFIFO > 0))
            {
              UTXOBatch batch = new UTXOBatch()
              {
                BatchIndex = BatchIndexLoad++,
                IsCancellationBatch = downloadBatch.IsCancellationBatch
              };

              int tXCountBatch = 0;
              while (
                tXCountBatch < COUNT_TXS_IN_BATCH_FILE ||
                (batch.IsCancellationBatch && TXCountFIFO > 0))
              {
                Block block = FIFOBlocks.Dequeue();
                batch.Blocks.Add(block);

                tXCountBatch += block.TXCount;
                TXCountFIFO -= block.TXCount;
              }
              
              BatchParserBuffer.Post(batch);
            }

            DownloadBatcherIndex += 1;

            if(QueueDownloadBatch.TryGetValue(DownloadBatcherIndex, out downloadBatch))
            {
              QueueDownloadBatch.Remove(DownloadBatcherIndex);
            }
            else
            {
              break;
            }

          } while (true);
        }
      }

      void CreateDownloadBatches()
      {
        int indexDownloadBatch = 0;
        
        while(true)
        {
          var downloadBatch = new UTXODownloadBatch(indexDownloadBatch++);
          
          for (int i = 0; i < COUNT_BLOCKS_DOWNLOAD_BATCH; i += 1)
          {
            Headerchain.ChainHeader headerSendToMergerNext = HeaderSentToMergerLast.HeadersNext[0];
            
            downloadBatch.HeaderHashes.Add(headerSendToMergerNext.GetHeaderHash(SHA256));

            if (headerSendToMergerNext.HeadersNext == null)
            {
              downloadBatch.IsCancellationBatch = true;
              DownloaderBuffer.Post(downloadBatch);
              return;
            }

            HeaderSentToMergerLast = headerSendToMergerNext;
          }

          DownloaderBuffer.Post(downloadBatch);
        }
      }
    }
  }
}
