using System;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Security.Cryptography;


namespace BToken.Chaining
{
  public partial class Blockchain
  {
    class BlockArchiver
    {
      readonly DirectoryInfo ArchiveDirectory =
        Directory.CreateDirectory("J:\\BlockArchivePartitioned");

      int SIZE_HEADER_ARCHIVE = 2000;
      int SIZE_BLOCK_ARCHIVE = 50000;

      FileStream HeaderArchive;
      int IndexHeaderArchive;
      int CountHeaders;

      FileStream FileBlockArchive;
      int IndexBlockArchiveLoad;
      int IndexBlockArchive;
      int CountTXs;
      

      public BlockArchiver()
      {
        Initialize();
      }

      public void Initialize()
      {
        if (HeaderArchive != null)
        {
          HeaderArchive.Dispose();
          HeaderArchive = null;
        }

        IndexHeaderArchive = 0;
        CountHeaders = 0;
        CreateHeaderArchive();

        if (FileBlockArchive != null)
        {
          FileBlockArchive.Dispose();
          FileBlockArchive = null;
        }

        IndexBlockArchive = 0;
        CreateBlockArchive();
      }


      public void Initialize(int archiveIndex)
      {
        IndexBlockArchive = archiveIndex;
        CreateBlockArchive();
      }

      public void Initialize(
        UTXOTable.BlockArchive blockArchive)
      {
        if (blockArchive.CountTX < SIZE_BLOCK_ARCHIVE)
        {
          IndexBlockArchive = blockArchive.Index;
          OpenBlockArchive();
        }
        else
        {
          IndexBlockArchive = blockArchive.Index + 1;
          CreateBlockArchive();
        }
      }


      public void LoadHeaderArchive(
        BranchInserter branch,
        byte[] stopHash,
        SHA256 sHA256)
      {
        HeaderContainer headerContainer = new HeaderContainer();

        while (true)
        {
          try
          {
            headerContainer.Buffer = ReadHeaderArchive();

            headerContainer.Parse(sHA256, stopHash);

            branch.StageHeaders(headerContainer.HeaderRoot);
          }
          catch
          {
            CreateHeaderArchive();
            return;
          }

          if (headerContainer.Count < SIZE_HEADER_ARCHIVE)
          {
            break;
          }

          IndexHeaderArchive += 1;

          if (stopHash.IsEqual(headerContainer.HeaderTip.Hash))
          {
            CreateHeaderArchive();
            return;
          }
        }

        if (stopHash.IsEqual(headerContainer.HeaderTip.Hash))
        {
          CreateHeaderArchive();

          HeaderArchive.Write(
            headerContainer.Buffer,
            0,
            headerContainer.Index);
        }
        else
        {
          OpenHeaderArchive();
        }
      }

      byte[] ReadHeaderArchive()
      {
        return File.ReadAllBytes(
          Path.Combine(
            "Headerchain",
            IndexHeaderArchive.ToString()));
      }

      void OpenHeaderArchive()
      {
        string filePathHeaderArchive = Path.Combine(
          ArchiveDirectory.FullName,
          "Headerchain",
          IndexHeaderArchive.ToString());

        HeaderArchive = new FileStream(
          filePathHeaderArchive,
          FileMode.Append,
          FileAccess.Write,
          FileShare.None,
          bufferSize: 65536);
      }

      void CreateHeaderArchive()
      {
        CountHeaders = 0;

        if (HeaderArchive != null)
        {
          HeaderArchive.Dispose();
        }

        string filePathHeaderArchive = Path.Combine(
          ArchiveDirectory.FullName,
          "Headerchain",
          IndexHeaderArchive.ToString());

        HeaderArchive = new FileStream(
          filePathHeaderArchive,
          FileMode.Create,
          FileAccess.Write,
          FileShare.None,
          bufferSize: 65536);
      }



      List<UTXOTable.BlockArchive> BlockContainers = 
        new List<UTXOTable.BlockArchive>();
      List<Header> HeaderArchiveQueue = new List<Header>();

      public bool ArchiveBlock(
        UTXOTable.BlockArchive blockArchive)
      {
        HeaderArchiveQueue.Add(blockArchive.HeaderRoot);

        if (HeaderArchiveQueue.Count >= SIZE_HEADER_ARCHIVE)
        {
          HeaderArchiveQueue.ForEach(
            h => WriteToArchive(HeaderArchive, h.GetBytes()));

          HeaderArchiveQueue.Clear();

          CreateHeaderArchive();
        }

        BlockContainers.Add(blockArchive);
        CountTXs += blockArchive.CountTX;

        if (CountTXs >= SIZE_BLOCK_ARCHIVE)
        {
          BlockContainers.ForEach(
            c => WriteToArchive(FileBlockArchive, c.Buffer));

          BlockContainers.Clear();
          IndexBlockArchive += 1;

          CreateBlockArchive();
        }
      }
            
      void CreateBlockArchive()
      {
        CountTXs = 0;

        if (FileBlockArchive != null)
        {
          FileBlockArchive.Dispose();
        }

        string filePathBlockArchive = Path.Combine(
          ArchiveDirectory.FullName,
          IndexBlockArchive.ToString());

        FileBlockArchive = new FileStream(
          filePathBlockArchive,
          FileMode.Create,
          FileAccess.Write,
          FileShare.None,
          bufferSize: 65536);
      }

      void OpenBlockArchive()
      {
        string filePathBlockArchive = Path.Combine(
          ArchiveDirectory.FullName,
          IndexBlockArchive.ToString());

        FileBlockArchive = new FileStream(
          filePathBlockArchive,
          FileMode.Append,
          FileAccess.Write,
          FileShare.None,
          bufferSize: 65536);
      }


      public void Write(
        List<UTXOTable.BlockArchive> blockArchives,
        string pathBlockArchive)
      {
        var fileBlockArchive = new FileStream(
          pathBlockArchive,
          FileMode.Create,
          FileAccess.Write,
          FileShare.None,
          bufferSize: 65536);

        blockArchives.ForEach(
          c => WriteToArchive(fileBlockArchive, c.Buffer));
      }

      static void WriteToArchive(
        FileStream file,
        byte[] bytes)
      {
        while (true)
        {
          try
          {
            file.Write(bytes, 0, bytes.Length);
            break;
          }
          catch (IOException ex)
          {
            Console.WriteLine(
              ex.GetType().Name + ": " + ex.Message);

            Thread.Sleep(2000);
            continue;
          }
        }
      }

                 
      public void Restore()
      {
        // Copy back the files from the rollbackFolder to main archive
      }
      public void CommitBranch()
      {
        // delete files in rollbackFolder
      }

      public void Branch(BlockArchiver archiver)
      {
        ArchiveDirectory.GetFiles().ToList()
          .ForEach(f => f.Delete());

        HeaderArchive = archiver.HeaderArchive;
        IndexHeaderArchive = archiver.IndexHeaderArchive;
        CountHeaders = archiver.IndexHeaderArchive;

        FileBlockArchive = archiver.FileBlockArchive;
        IndexBlockArchive = archiver.IndexBlockArchive;
        CountTXs = archiver.CountTXs;
      }

      public void Export(BlockArchiver archiveDestination)
      {
        FileInfo[] files = ArchiveDirectory.GetFiles();

        int i = 0;
        while (i < files.Length)
        {
          string nameFileDest = Path.Combine(
                archiveDestination.ArchiveDirectory.Name,
                files[i].Name);
          try
          {
            files[i].MoveTo(nameFileDest);
          }
          catch (IOException)
          {
            File.Delete(nameFileDest);
            continue;
          }

          i += 1;
        }

        archiveDestination.BlockContainers = BlockContainers;
        archiveDestination.CountTXs = CountTXs;
        archiveDestination.IndexBlockArchive = IndexBlockArchive;
      }
    }
  }
}
