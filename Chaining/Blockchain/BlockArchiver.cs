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
    class BlockArchiver
    {
      Blockchain Blockchain;

      public int IndexBlockArchive;

      byte[] HashStopLoading;
      public const int COUNT_LOADER_TASKS = 6;
      int SIZE_BLOCK_ARCHIVE = 20000;
      const int UTXOIMAGE_INTERVAL_LOADER = 200;

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

      public async Task LoadBlocks(
        byte[] hashStopLoading,
        int indexBlockArchive)
      {
        "Start archive loader".Log(LogFile);

        IsBlockLoadingCompleted = false;

        IndexBlockArchiveLoad = indexBlockArchive;
        IndexBlockArchiveQueue = indexBlockArchive;
        HashStopLoading = hashStopLoading;

        Task inserterTask = RunLoaderInserter();

        var loaderTasks = new Task[COUNT_LOADER_TASKS];

        Parallel.For(
          0,
          COUNT_LOADER_TASKS,
          i => loaderTasks[i] = StartLoader());

        await Task.WhenAll(loaderTasks);

        await inserterTask;
      }
      
      BufferBlock<UTXOTable.BlockParser> QueueLoader =
        new BufferBlock<UTXOTable.BlockParser>();

      async Task RunLoaderInserter()
      {
        "Start archive inserter.".Log(LogFile);
        
        while (true)
        {
          UTXOTable.BlockParser parser = await QueueLoader
            .ReceiveAsync()
            .ConfigureAwait(false);

          IndexBlockArchive = parser.Index;

          if (
            parser.IsInvalid ||
            !parser.HeaderRoot.HashPrevious.IsEqual(
              Blockchain.HeaderTip.Hash))
          {
            CountTXsArchive = 0;
            CreateBlockArchive();

            break;
          }

          try
          {
            Blockchain.InsertBlocks(
              parser,
              parser.Index,
              flagValidateHeaders: true);
          }
          catch (ChainException ex)
          {
            string.Format(
              "Exception when inserting blockArchive {0}: \n{1}",
              parser.Index, ex.Message)
              .Log(LogFile);

            File.Delete(
              Path.Combine(
                ArchiveDirectoryBlocks.Name,
                parser.Index.ToString()));

            CountTXsArchive = 0;
            CreateBlockArchive();

            break;
          }
          catch(Exception ex)
          {
            Console.WriteLine(
              "Archive inserter aborted due to {0}:\n{1}",
              ex.GetType().Name,
              ex.Message);

            return;
          }

          if (parser.CountTX < SIZE_BLOCK_ARCHIVE)
          {
            CountTXsArchive = parser.CountTX;
            OpenBlockArchive();

            break;
          }

          IndexBlockArchive += 1;

          if (parser.HeaderTip.Hash
            .IsEqual(HashStopLoading))
          {
            CountTXsArchive = 0;
            CreateBlockArchive();

            break;
          }

          if (IndexBlockArchive % UTXOIMAGE_INTERVAL_LOADER == 0)
          {
            Blockchain.CreateImage(IndexBlockArchive);
          }

          Blockchain.ReleaseBlockParser(parser);
        }

        string.Format("Blockloading completed.")
          .Log(LogFile);

        IsBlockLoadingCompleted = true;
      }


      List<UTXOTable.BlockParser> BlockArchives =
        new List<UTXOTable.BlockParser>();
      int CountTXsArchive;
      FileStream FileBlockArchive;

      public void ArchiveBlock(
        UTXOTable.BlockParser blockParser,
        int intervalImage)
      {
        while (true)
        {
          try
          {
            FileBlockArchive.Write(
              blockParser.Buffer,
              0,
              blockParser.IndexBuffer);

            FileBlockArchive.Flush();

            break;
          }
          catch (Exception ex)
          {
            string.Format(
              "{0} when writing blockArchive {1} to " +
              "file {2}: \n{3} \n" +
              "Try again in 10 seconds ...",
              ex.GetType().Name, blockParser.Index,
              FileBlockArchive.Name, ex.Message)
              .Log(LogFile);

            Thread.Sleep(10000);
          }
        }

        CountTXsArchive += blockParser.CountTX;

        if (CountTXsArchive >= SIZE_BLOCK_ARCHIVE)
        {
          FileBlockArchive.Dispose();

          CountTXsArchive = 0;

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
      }

      public void Dispose()
      {
        FileBlockArchive.Dispose();
      }


      bool IsBlockLoadingCompleted;
      Dictionary<int, UTXOTable.BlockParser> QueueBlockArchives =
        new Dictionary<int, UTXOTable.BlockParser>();
      readonly object LOCK_QueueBlockArchives = new object();

      async Task StartLoader()
      {
        UTXOTable.BlockParser parser = null;

      LABEL_LoadBlockArchive:

        if (parser == null)
        {
          parser = Blockchain.GetBlockParser();
        }

        lock (LOCK_IndexBlockArchiveLoad)
        {
          parser.Index = IndexBlockArchiveLoad;
          IndexBlockArchiveLoad += 1;
        }

        string pathFile = Path.Combine(
          ArchiveDirectoryBlocks.FullName,
          parser.Index.ToString());

        byte[] bytesFile;
                
        try
        {
          bytesFile = File.ReadAllBytes(pathFile);
          
          parser.Parse(bytesFile, HashStopLoading);

          parser.IsInvalid = false;
        }
        catch(Exception ex)
        {
          parser.IsInvalid = true;

          string.Format(
            "Loader throws exception {0} \n" +
            "when parsing file {1}",
            pathFile,
            ex.Message)
            .Log(LogFile);
        }
        
        while (true)
        {
          if (IsBlockLoadingCompleted)
          {
            string.Format(
              "Loader worker {0} exit",
              Thread.CurrentThread.ManagedThreadId)
              .Log(LogFile);

            Blockchain.ReleaseBlockParser(parser);

            return;
          }

          if(QueueLoader.Count < COUNT_LOADER_TASKS)
          {
            lock (LOCK_QueueBlockArchives)
            {
              if (parser.Index == IndexBlockArchiveQueue)
              {
                break;
              }

              if (QueueBlockArchives.Count <= COUNT_LOADER_TASKS)
              {
                QueueBlockArchives.Add(
                  parser.Index,
                  parser);

                if (parser.IsInvalid)
                {
                  return;
                }

                parser = null;

                goto LABEL_LoadBlockArchive;
              }
            }
          }

          await Task.Delay(2000).ConfigureAwait(false);
        }

        while (true)
        {
          QueueLoader.Post(parser);

          if (parser.IsInvalid)
          {
            return;
          }

          parser = null;

          lock (LOCK_QueueBlockArchives)
          {
            IndexBlockArchiveQueue += 1;

            if (QueueBlockArchives.TryGetValue(
              IndexBlockArchiveQueue,
              out parser))
            {
              QueueBlockArchives.Remove(parser.Index);
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
