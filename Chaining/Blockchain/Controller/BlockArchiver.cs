using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.IO;
using System.Text.RegularExpressions;

using BToken.Networking;

namespace BToken.Chaining
{
  public partial class BlockArchiver
  {
    readonly static string ArchiveRootPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "BlockArchive");
    DirectoryInfo RootDirectory;

    const int ARCHIVE_COUNT_PER_DIRECTORY = 0x4;
    string DirectoryHandle = "Shelf";
    DirectoryInfo DirectoryActive;
    uint DirectoryEnumerator;

    const int ARCHIVE_ITEM_BYTESIZE_MAX = 0x400000;
    string FileHandle = "Block";
    FileStream FileStream;
    uint FileEnumerator;


    public BlockArchiver()
    {
      RootDirectory = Directory.CreateDirectory(ArchiveRootPath);

      DirectoryEnumerator = GetDirectoryEnumerator();
      DirectoryActive = CreateDirectory();

      FileEnumerator = GetFileEnumerator();
      FileStream = CreateFile();
    }

    uint GetDirectoryEnumerator()
    {
      uint directoryEnumerator = 0;

      DirectoryInfo[] directories = RootDirectory.GetDirectories();
      foreach (DirectoryInfo directory in directories)
      {
        uint enumerator = uint.Parse(Regex.Match(directory.Name, @"\d+$").Value);
        if (enumerator >= directoryEnumerator)
        {
          directoryEnumerator = enumerator;
        }
      }

      return directoryEnumerator;
    }
    DirectoryInfo CreateDirectory()
    {
      return Directory.CreateDirectory(Path.Combine(RootDirectory.Name, DirectoryHandle + DirectoryEnumerator));
    }

    uint GetFileEnumerator()
    {
      uint fileEnumerator = 0;

      FileInfo[] fileInfos = DirectoryActive.GetFiles(".dat");
      foreach (FileInfo fileInfo in fileInfos)
      {
        uint enumerator = uint.Parse(Regex.Match(fileInfo.Name, @"\d+$").Value);
        if (enumerator >= fileEnumerator)
        {
          fileEnumerator = enumerator;
        }
      }

      return fileEnumerator;
    }
    FileStream CreateFile()
    {
      return new FileStream(
        Path.Combine(ArchiveRootPath, DirectoryActive.Name, FileHandle + FileEnumerator),
        FileMode.OpenOrCreate,
        FileAccess.ReadWrite,
        FileShare.Read,
        ARCHIVE_ITEM_BYTESIZE_MAX
        );
    }

    public BlockStore ArchiveBlock(NetworkBlock block)
    {
      byte[] headerBytes = block.Header.getBytes();
      int blockByteSize = headerBytes.Length + block.Payload.Length;

      if (blockByteSize > ARCHIVE_ITEM_BYTESIZE_MAX)
      {
        throw new BlockchainException(string.Format("Network block too big. Maximum size '{0}', current size '{1}'", ARCHIVE_ITEM_BYTESIZE_MAX, blockByteSize));
      }

      if (FileStream.Length + blockByteSize > ARCHIVE_ITEM_BYTESIZE_MAX)
      {
        CreateNewArchive();
      }

      FileStream.Write(headerBytes, 0, headerBytes.Length);
      FileStream.Write(block.Payload, 0, block.Payload.Length);

      return new BlockStore()
      {
        DirectoryEnumerator = DirectoryEnumerator,
        FileEnumerator = FileEnumerator
      };
    }

    void CreateNewArchive()
    {
      FileStream.Close();

      if (FileEnumerator >= ARCHIVE_COUNT_PER_DIRECTORY -1 )
      {
        FileEnumerator = 0;

        DirectoryEnumerator++;
        DirectoryActive = CreateDirectory();
      }
      else
      {
        FileEnumerator++;
      }

      FileStream = CreateFile();
    }
  }
}
