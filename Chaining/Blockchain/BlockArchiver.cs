﻿using System;
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
      const int COUNT_LOADER_TASKS = 1;
      int SIZE_BLOCK_ARCHIVE = 20000;
      const int UTXOIMAGE_INTERVAL_LOADER = 500;

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


      BufferBlock<UTXOTable.BlockArchive> QueueLoader =
        new BufferBlock<UTXOTable.BlockArchive>();

      async Task RunLoaderInserter()
      {
        "Start archive inserter".Log(LogFile);

        while (true)
        {
          UTXOTable.BlockArchive blockArchive =
            await QueueLoader.ReceiveAsync()
            .ConfigureAwait(false);

          string.Format("inserter receives blockArchive {0}",
            blockArchive.Index)
            .Log(LogFile);

          if (
            blockArchive.IsInvalid ||
            !blockArchive.HeaderRoot.HashPrevious.IsEqual(
              Blockchain.HeaderTip.Hash))
          {
            CountTXsArchive = 0;
            CreateBlockArchive(IndexBlockArchive);

            break;
          }

          try
          {
            blockArchive.HeaderRoot.HeaderPrevious = Blockchain.HeaderTip;

            Blockchain.ValidateHeaders(blockArchive.HeaderRoot);

            Blockchain.InsertBlockArchive(blockArchive);

            if (blockArchive.CountTX < SIZE_BLOCK_ARCHIVE)
            {
              CountTXsArchive = blockArchive.CountTX;
              OpenBlockArchive(IndexBlockArchive);

              break;
            }

            IndexBlockArchive += 1;

            if (blockArchive.HeaderTip.Hash.IsEqual(HashStopLoading))
            {
              CountTXsArchive = 0;
              CreateBlockArchive(IndexBlockArchive);

              break;
            }

            if (IndexBlockArchive % UTXOIMAGE_INTERVAL_LOADER == 0)
            {
              Blockchain.CreateImage(IndexBlockArchive);
            }
          }
          catch (ChainException ex)
          {
            string.Format(
              "exception when inserting blockArchive {0}: \n{1}",
              blockArchive.Index, ex.Message)
              .Log(LogFile);

            File.Delete(
              Path.Combine(
                ArchiveDirectoryBlocks.Name,
                blockArchive.Index.ToString()));

            CountTXsArchive = 0;
            CreateBlockArchive(IndexBlockArchive);
          }

          string.Format("inserted blockArchive {0}",
            blockArchive.Index)
            .Log(LogFile);
        }

        string.Format("Blockloading complete")
          .Log(LogFile);

        IsBlockLoadingCompleted = true;
      }


      List<UTXOTable.BlockArchive> BlockArchives =
        new List<UTXOTable.BlockArchive>();
      int CountTXsArchive;
      FileStream FileBlockArchive;

      public void ArchiveBlock(
        UTXOTable.BlockArchive blockArchive,
        int intervalImage)
      {
        while (true)
        {
          try
          {
            FileBlockArchive.Write(
              blockArchive.Buffer,
              0,
              blockArchive.IndexBuffer);

            FileBlockArchive.Flush();

            break;
          }
          catch (Exception ex)
          {
            string message = string.Format(
              "Exception {0} when writing blockArchive {1} to " +
              "file {2}: \n{3} \n" +
              "Try again in 10 seconds ...",
              ex.GetType().Name,
              blockArchive.Index,
              FileBlockArchive.Name,
              ex.Message);

            Console.WriteLine(message);
            message.Log(LogFile);

            Thread.Sleep(10000);
          }
        }

        CountTXsArchive += blockArchive.CountTX;

        if (CountTXsArchive >= SIZE_BLOCK_ARCHIVE)
        {
          FileBlockArchive.Dispose();

          CountTXsArchive = 0;

          IndexBlockArchive += 1;

          if (IndexBlockArchive % intervalImage == 0)
          {
            Blockchain.CreateImage(IndexBlockArchive);
          }

          CreateBlockArchive(IndexBlockArchive);
        }
      }

      void OpenBlockArchive(int indexArchive)
      {
        Console.WriteLine("Open BlockArchive {0}", indexArchive);

        string.Format("Open BlockArchive {0}",
          indexArchive)
          .Log(LogFile);

        string pathFileArchive = Path.Combine(
          ArchiveDirectoryBlocks.FullName,
          indexArchive.ToString());

        FileBlockArchive = new FileStream(
         pathFileArchive,
         FileMode.Append,
         FileAccess.Write,
         FileShare.None,
         bufferSize: 65536);
      }

      void CreateBlockArchive(int indexArchive)
      {
        Console.WriteLine("Create BlockArchive {0}", indexArchive);

        string.Format("Create BlockArchive {0}",
          indexArchive)
          .Log(LogFile);

        string pathFileArchive = Path.Combine(
          ArchiveDirectoryBlocks.FullName,
          indexArchive.ToString());

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
      Dictionary<int, UTXOTable.BlockArchive> QueueBlockArchives =
        new Dictionary<int, UTXOTable.BlockArchive>();
      readonly object LOCK_QueueBlockArchives = new object();

      async Task StartLoader()
      {
        string.Format("Start Loader worker {0}", 
          Thread.CurrentThread.ManagedThreadId)
          .Log(LogFile);

        UTXOTable.BlockArchive blockArchive = null;

      LABEL_LoadBlockArchive:

        LoadBlockArchive(ref blockArchive);

        blockArchive.Reset();

        try
        {
          string pathFile = Path.Combine(
            ArchiveDirectoryBlocks.FullName,
            blockArchive.Index.ToString());

          blockArchive.Parse(
            File.ReadAllBytes(pathFile),
            HashStopLoading);
        }
        catch
        {
          blockArchive.IsInvalid = true;
        }

        string.Format(
          "Loader worker {0} loaded " +
          "blockArchive {1} which is {2}",
          Thread.CurrentThread.ManagedThreadId,
          blockArchive.Index,
          blockArchive.IsInvalid ? "Invalid" : "Valid")
          .Log(LogFile);

        while (true)
        {
          if (IsBlockLoadingCompleted)
          {
            string.Format(
              "Loader worker {0} exit",
              Thread.CurrentThread.ManagedThreadId)
              .Log(LogFile);

            return;
          }

          lock (LOCK_QueueBlockArchives)
          {
            if (blockArchive.Index == IndexBlockArchiveQueue)
            {
              break;
            }

            if (QueueBlockArchives.Count <= COUNT_LOADER_TASKS)
            {
              QueueBlockArchives.Add(
                blockArchive.Index,
                blockArchive);

              if (blockArchive.IsInvalid)
              {
                return;
              }

              blockArchive = null;

              goto LABEL_LoadBlockArchive;
            }
          }

          await Task.Delay(2000).ConfigureAwait(false);
        }

        while (true)
        {
          QueueLoader.Post(blockArchive);

          if (blockArchive.IsInvalid)
          {
            return;
          }

          blockArchive = null;

          lock (LOCK_QueueBlockArchives)
          {
            IndexBlockArchiveQueue += 1;

            if (QueueBlockArchives.TryGetValue(
              IndexBlockArchiveQueue,
              out blockArchive))
            {
              QueueBlockArchives.Remove(blockArchive.Index);
            }
            else
            {
              goto LABEL_LoadBlockArchive;
            }
          }
        }
      }

      readonly object LOCK_BlockArchivesIdle = new object();
      List<UTXOTable.BlockArchive> BlockArchivesIdle =
        new List<UTXOTable.BlockArchive>();

      void LoadBlockArchive(
        ref UTXOTable.BlockArchive blockArchive)
      {
        if (blockArchive == null)
        {
          lock (LOCK_BlockArchivesIdle)
          {
            if (BlockArchivesIdle.Count == 0)
            {
              blockArchive = new UTXOTable.BlockArchive();
            }
            else
            {
              blockArchive = BlockArchivesIdle.Last();
              BlockArchivesIdle.Remove(blockArchive);
            }
          }
        }

        lock (LOCK_IndexBlockArchiveLoad)
        {
          blockArchive.Index = IndexBlockArchiveLoad;
          IndexBlockArchiveLoad += 1;
        }

        blockArchive.IndexBuffer = 0;
      }
    }
  }
}