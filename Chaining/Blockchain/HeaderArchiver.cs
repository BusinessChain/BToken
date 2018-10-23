using System;
using System.Collections.Generic;
using System.Linq;
using System.IO;
using System.Threading.Tasks;

using BToken.Networking;

namespace BToken.Chaining
{
  partial class Blockchain
  {
    class HeaderArchiver
    {
      readonly static string ArchiveRootPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "HeaderArchive");
      static DirectoryInfo RootDirectory = Directory.CreateDirectory(ArchiveRootPath);

      BlockchainController Controller;
      string FilePath;


      public HeaderArchiver(BlockchainController controller)
      {
        Controller = controller;
        FilePath = Path.Combine(RootDirectory.Name, "Headerchain");
      }


      public class HeaderWriter : IDisposable
      {
        FileStream FileStream;


        public HeaderWriter(HeaderArchiver archiver)
        {
          FileStream = new FileStream(
            archiver.FilePath,
            FileMode.Append,
            FileAccess.Write,
            FileShare.None);
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
      public class HeaderReader : IDisposable
      {
        FileStream FileStream;
        
        
        public HeaderReader(HeaderArchiver archiver)
        {
          FileStream = new FileStream(
            archiver.FilePath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read);
        }

        public NetworkHeader GetNextHeader()
        {
          byte[] headerBytes = new byte[81];
          int bytesReadCount = FileStream.Read(headerBytes, 0, 80);

          if(bytesReadCount < 80)
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
