using System;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using System.Threading;

using BToken.Networking;

namespace BToken.Chaining
{
  public partial class Headerchain
  {
    class HeaderArchiver : IHeaderArchiver
    {
      readonly static string ArchiveRootPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "HeaderArchive");
      static DirectoryInfo RootDirectory = Directory.CreateDirectory(ArchiveRootPath);
      static string FilePath = Path.Combine(RootDirectory.Name, "Headerchain");

      public IHeaderWriter GetWriter()
      {
        return new HeaderWriter();
      }
      public IHeaderReader GetReader()
      {
        return new HeaderReader();
      }

      class HeaderWriter : IHeaderWriter, IDisposable
      {
        FileStream FileStream;


        public HeaderWriter()
        {
          FileStream = WaitForFile(
            FilePath,
            FileMode.Append,
            FileAccess.Write,
            FileShare.None);
        }

        static FileStream WaitForFile(string fullPath, FileMode fileMode, FileAccess fileAccess, FileShare fileShare)
        {
          for (int numTries = 0; numTries < 10; numTries++)
          {
            FileStream fs = null;
            try
            {
              fs = new FileStream(fullPath, fileMode, fileAccess, fileShare);
              return fs;
            }
            catch (IOException)
            {
              if (fs != null)
              {
                fs.Dispose();
              }
              Thread.Sleep(50);
            }
          }

          throw new IOException(string.Format("File '{0}' cannot be accessed because it is blocked by another process.", fullPath));
        }

        public void StoreHeader(NetworkHeader header)
        {
          byte[] headerBytes = header.GetBytes();
          FileStream.Write(headerBytes, 0, headerBytes.Length);
        }

        public void Dispose()
        {
          FileStream.Dispose();
        }
      }

      class HeaderReader : IHeaderReader, IDisposable
      {
        FileStream FileStream;

        public HeaderReader()
        {
          FileStream = new FileStream(
            FilePath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read);
        }

        public NetworkHeader GetNextHeader()
        {
          byte[] headerBytes = new byte[81];
          int bytesReadCount = FileStream.Read(headerBytes, 0, 80);

          if (bytesReadCount < 80)
          {
            return null;
          }

          int startIndex = 0;
          return NetworkHeader.ParseHeader(headerBytes, out int txCount, ref startIndex);
        }

        public void Dispose()
        {
          FileStream.Dispose();
        }
      }
    }
  }
}
