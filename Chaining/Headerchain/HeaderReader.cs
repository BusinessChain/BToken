using System;
using System.Collections.Generic;
using System.Linq;
using System.IO;

using BToken.Networking;

namespace BToken.Chaining
{
  public partial class Headerchain
  {
    class HeaderReader : IDisposable
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
