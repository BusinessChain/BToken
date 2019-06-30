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

      const int COUNT_PROCESSING_BATCHES_PARALLEL = 8;
      const int COUNT_DOWNLOAD_TASKS_PARALLEL = 8;
      const int COUNT_BLOCKS_DOWNLOAD_BATCH = 10;
      const int COUNT_TXS_IN_BATCH_FILE = 100;

      CancellationTokenSource CancellationBuilder 
        = new CancellationTokenSource();

      readonly object LOCK_BatchIndexLoad = new object();
      int BatchIndexLoad;
      readonly object LOCK_BatchIndexMerge = new object();
      int DownloadIndex;
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
      
      byte[] HeaderHashDispatchedLast;


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
        
        StartNetworkLoader();

        await DelayUntilMergerCancelsAsync();
      }
      async Task DelayUntilMergerCancelsAsync()
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

          Merger.BatchIndexNext = 0;
          Merger.BlockHeight = 0;
          Merger.HeaderHashMergedLast = UTXO.Headerchain.GenesisHeader.GetHeaderHash();
        }

        BatchIndexLoad = Merger.BatchIndexNext;
      }
      void InsertGenesisBlock()
      {
        UTXOBatch genesisBatch = new UTXOBatch()
        {
          BatchIndex = 0,
          Buffer = GenesisBlock.BlockBytes
        };

        var parser = new UTXOParser(UTXO);
        parser.Load(genesisBatch);
        parser.ParseBatch();

        UTXO.InsertUTXOs(genesisBatch.Blocks.First());
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
            UTXOBatch batch = new UTXOBatch();

            lock (LOCK_BatchIndexLoad)
            {
              batch.BatchIndex = BatchIndexLoad;
              BatchIndexLoad += 1;
            }

            try
            {
              batch.Buffer = await BlockArchiver
              .ReadBlockBatchAsync(batch.BatchIndex).ConfigureAwait(false);
            }
            catch(IOException)
            {
              lock (LOCK_BatchIndexLoad)
              {
                BatchIndexLoad -= 1;
                return;
              }
            }

            batch.StopwatchParse.Start();
            parser.Load(batch);
            parser.ParseBatch();
            batch.StopwatchParse.Stop();

            Merger.BatchBuffer.Post(batch);
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
        if(IsChainInSyncWithNetwork(out Headerchain.ChainHeader header))
        {
          return;
        }

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

        CreateDownloadBatches(header);
      }

      bool IsChainInSyncWithNetwork(out Headerchain.ChainHeader headerDispatchedLast)
      {
        headerDispatchedLast = UTXO.Headerchain.ReadHeader(
          HeaderHashDispatchedLast, 
          SHA256);

        if (headerDispatchedLast.HeadersNext == null)
        {
          return true;
        }

        return false;
      }

      async Task RunTXParserAsync()
      {
        UTXOParser parser = new UTXOParser(UTXO);
        
        while(true)
        {
          UTXOBatch batch = await BatchParserBuffer
            .ReceiveAsync(CancellationBuilder.Token).ConfigureAwait(false);

          parser.Load(batch);
          parser.ParseBatch();

          await BlockArchiver.ArchiveBatchAsync(batch.Blocks, batch.BatchIndex);

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

          while (true)
          {
            foreach (Block block in downloadBatch.Blocks)
            {
              FIFOBlocks.Enqueue(block);
              TXCountFIFO += block.TXCount;
            }

            while(
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

            if (!QueueDownloadBatch.TryGetValue(DownloadBatcherIndex, out downloadBatch))
            {
              break;
            }
          }
        }
      }

      void CreateDownloadBatches(Headerchain.ChainHeader header)
      {
        int indexDownloadBatch = 0;
        
        while(true)
        {
          var downloadBatch = new UTXODownloadBatch(indexDownloadBatch++);

          for (int i = 0; i < COUNT_BLOCKS_DOWNLOAD_BATCH; i += 1)
          {
            downloadBatch.HeaderHashes.Add(header.GetHeaderHash(SHA256));

            if (header.HeadersNext == null)
            {
              downloadBatch.IsCancellationBatch = true;
              DownloaderBuffer.Post(downloadBatch);
              return;
            }

            header = header.HeadersNext[0];
          }

          DownloaderBuffer.Post(downloadBatch);
        }
      }
    }
  }
}
