using System;
using System.IO;
using System.Threading;

using BToken.Networking;

namespace BToken.Chaining
{
  public partial class Headerchain
  {
    public class HeaderWriter : IDisposable
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
            return new FileStream(
              fullPath, 
              fileMode, 
              fileAccess,
              fileShare,
              bufferSize: 4096,
              useAsync: true);
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
        FileStream.Write(headerBytes, 0, headerBytes.Length - 1);
      }

      public void Dispose()
      {
        FileStream.Dispose();
      }
    }
  }
}
