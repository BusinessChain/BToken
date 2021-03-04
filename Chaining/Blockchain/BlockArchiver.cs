using System;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Threading.Tasks.Dataflow;

namespace BToken.Chaining
{
  partial class Blockchain
  {
    partial class BlockArchiver
    {
      Blockchain Blockchain;

      public int IndexBlockArchive;

      byte[] HashStopLoadingInclusive;
      public const int COUNT_LOADER_TASKS = 4;
      int SIZE_BLOCK_ARCHIVE = 20000;
      const int UTXOIMAGE_INTERVAL_LOADER = 100;

      readonly object LOCK_IndexBlockArchiveQueue = new object();
      int IndexBlockArchiveQueue;

      DirectoryInfo ArchiveDirectoryBlocks =
          Directory.CreateDirectory("J:\\BlockArchivePartitioned");

      StreamWriter LogFile;



      public BlockArchiver(Blockchain blockchain)
      {
        Blockchain = blockchain;

        LogFile = new StreamWriter("logArchiver", false);
      }



      readonly object LOCK_IndexBlockArchiveLoad = new object();
      int IndexBlockArchiveLoad;

      public async Task<bool> TryLoadBlocks(
        byte[] hashStopInclusive,
        int indexBlockArchive)
      {
        "Start archive loader".Log(LogFile);

        IsInserterCompleted = false;

        IndexBlockArchiveLoad = indexBlockArchive;
        IndexBlockArchiveQueue = indexBlockArchive;
        HashStopLoadingInclusive = hashStopInclusive;

        Task inserterTask = RunLoaderInserter();

        var loaderTasks = new Task[COUNT_LOADER_TASKS];

        Parallel.For(
          0,
          COUNT_LOADER_TASKS,
          i => loaderTasks[i] = StartLoader());
        
        await inserterTask;

        IsInserterCompleted = true;

        await Task.WhenAll(loaderTasks);

        return IsInserterSuccess;
      }
      
      BufferBlock<BlockArchiveLoad> QueueLoader =
        new BufferBlock<BlockArchiveLoad>();
      bool IsInserterSuccess;

      async Task RunLoaderInserter()
      {
        "Start archive inserter.".Log(LogFile);

        IsInserterSuccess = true;

        while (true)
        {
          BlockArchiveLoad blockArchiveLoad = await QueueLoader
            .ReceiveAsync()
            .ConfigureAwait(false);

          IndexBlockArchive = blockArchiveLoad.Index;

          if (
            blockArchiveLoad.IsInvalid ||
            !Blockchain.HeaderTip.Hash.IsEqual(
              blockArchiveLoad.Blocks.First().Header.HashPrevious))
          {
            CreateBlockArchive();
            break;
          }

          try
          {
            foreach (Block block in blockArchiveLoad.Blocks)
            {
              Blockchain.InsertBlock(
                block,
                flagValidateHeader: true);
            }
          }
          catch (ChainException ex)
          {
            string.Format(
              "Exception when inserting blockArchiveLoad {0}: \n{1}",
              blockArchiveLoad.Index,
              ex.Message)
              .Log(LogFile);

            File.Delete(
              Path.Combine(
                ArchiveDirectoryBlocks.Name,
                blockArchiveLoad.Index.ToString()));

            IsInserterSuccess = false;
            return;
          }
          
          if (blockArchiveLoad.CountTX < SIZE_BLOCK_ARCHIVE)
          {
            CountTXsArchive = blockArchiveLoad.CountTX;
            OpenBlockArchive();

            return;
          }

          IndexBlockArchive += 1;

          if (blockArchiveLoad.Blocks.Last().Header.Hash
            .IsEqual(HashStopLoadingInclusive))
          {
            CreateBlockArchive();

            return;
          }

          if (IndexBlockArchive % UTXOIMAGE_INTERVAL_LOADER == 0)
          {
            Blockchain.CreateImage(IndexBlockArchive);
          }
        }
      }


      List<UTXOTable.BlockParser> BlockArchives =
        new List<UTXOTable.BlockParser>();
      int CountTXsArchive;
      FileStream FileBlockArchive;

      public void ArchiveBlock(
        Block block,
        int intervalImage)
      {
        while (true)
        {
          try
          {
            FileBlockArchive.Write(
              block.Buffer,
              0,
              block.Buffer.Length);

            FileBlockArchive.Flush();

            break;
          }
          catch (Exception ex)
          {
            string.Format(
              "{0} when writing block {1} to " +
              "file {2}: \n{3} \n" +
              "Try again in 10 seconds ...",
              ex.GetType().Name, 
              block.Header.Hash.ToHexString(),
              FileBlockArchive.Name, 
              ex.Message)
              .Log(LogFile);

            Thread.Sleep(10000);
          }
        }

        CountTXsArchive += block.TXs.Count;

        if (CountTXsArchive >= SIZE_BLOCK_ARCHIVE)
        {
          FileBlockArchive.Dispose();

          IndexBlockArchive += 1;

          if (IndexBlockArchive % intervalImage == 0)
          {
            Blockchain.CreateImage(IndexBlockArchive);
          }

          CreateBlockArchive();
        }
      }

      void OpenBlockArchive()
      {
        string.Format(
          "Open BlockArchive {0}", 
          IndexBlockArchive)
          .Log(LogFile);

        string pathFileArchive = Path.Combine(
          ArchiveDirectoryBlocks.FullName,
          IndexBlockArchive.ToString());

        FileBlockArchive = new FileStream(
         pathFileArchive,
         FileMode.Append,
         FileAccess.Write,
         FileShare.None,
         bufferSize: 65536);
      }

      void CreateBlockArchive()
      {
        string pathFileArchive = Path.Combine(
          ArchiveDirectoryBlocks.FullName,
          IndexBlockArchive.ToString());

        FileBlockArchive = new FileStream(
         pathFileArchive,
         FileMode.Create,
         FileAccess.Write,
         FileShare.None,
         bufferSize: 65536);

        CountTXsArchive = 0;
      }

      public void Dispose()
      {
        FileBlockArchive.Dispose();
      }


      bool IsInserterCompleted;
      Dictionary<int, BlockArchiveLoad> QueueBlockArchives =
        new Dictionary<int, BlockArchiveLoad>();
      readonly object LOCK_QueueBlockArchives = new object();

      async Task StartLoader()
      {
        var parser = new UTXOTable.BlockParser();

      LABEL_LoadBlockArchive:

        var blockArchiveLoad = new BlockArchiveLoad();

        lock (LOCK_IndexBlockArchiveLoad)
        {
          blockArchiveLoad.Index = IndexBlockArchiveLoad;
          IndexBlockArchiveLoad += 1;
        }

        string pathFile = Path.Combine(
          ArchiveDirectoryBlocks.FullName,
          blockArchiveLoad.Index.ToString());

        try
        {
          byte[] bytesFile = File.ReadAllBytes(pathFile);
          int startIndex = 0;
          Block block;

          do
          {
            block = parser.ParseBlock(
              bytesFile,
              ref startIndex);
            
            blockArchiveLoad.InsertBlock(block);
          } while (
          startIndex < bytesFile.Length &&
          !HashStopLoadingInclusive.IsEqual(block.Header.Hash));

          blockArchiveLoad.IsInvalid = false;
        }
        catch(Exception ex)
        {
          blockArchiveLoad.IsInvalid = true;

          string.Format(
            "Loader throws exception {0} \n" +
            "when parsing file {1}",
            pathFile,
            ex.Message)
            .Log(LogFile);
        }
        
        while (true)
        {
          if (IsInserterCompleted)
          {
            string.Format(
              "Loader worker {0} exit",
              Thread.CurrentThread.ManagedThreadId)
              .Log(LogFile);

            return;
          }

          if(QueueLoader.Count < COUNT_LOADER_TASKS)
          {
            lock (LOCK_QueueBlockArchives)
            {
              if (blockArchiveLoad.Index == IndexBlockArchiveQueue)
              {
                break;
              }

              if (QueueBlockArchives.Count <= COUNT_LOADER_TASKS)
              {
                QueueBlockArchives.Add(
                  blockArchiveLoad.Index,
                  blockArchiveLoad);

                if (blockArchiveLoad.IsInvalid)
                {
                  return;
                }

                goto LABEL_LoadBlockArchive;
              }
            }
          }

          await Task.Delay(2000).ConfigureAwait(false);
        }

        while (true)
        {
          QueueLoader.Post(blockArchiveLoad);

          if (blockArchiveLoad.IsInvalid)
          {
            return;
          }

          lock (LOCK_QueueBlockArchives)
          {
            IndexBlockArchiveQueue += 1;

            if (QueueBlockArchives.TryGetValue(
              IndexBlockArchiveQueue,
              out blockArchiveLoad))
            {
              QueueBlockArchives.Remove(
                blockArchiveLoad.Index);
            }
            else
            {
              goto LABEL_LoadBlockArchive;
            }
          }
        }
      }
    }
  }
}
