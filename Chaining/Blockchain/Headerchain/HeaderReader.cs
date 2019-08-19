using System;
using System.Collections.Generic;
using System.Linq;
using System.IO;

using BToken.Networking;

namespace BToken.Chaining
{
  public partial class Blockchain
  {
    public partial class Headerchain
    {
      class HeaderReader : IDisposable
      {
        const int LENGTH_HEADER_BYTES = 80;
        FileStream FileStream;

        public HeaderReader()
        {
          FileStream = new FileStream(
            FilePath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read);
        }

        public NetworkHeader GetNextHeader(out byte[] headerBytes)
        {
          headerBytes = new byte[LENGTH_HEADER_BYTES];
          int bytesReadCount = FileStream.Read(headerBytes, 0, LENGTH_HEADER_BYTES);

          if (bytesReadCount < LENGTH_HEADER_BYTES)
          {
            return null;
          }

          int startIndex = 0;
          return NetworkHeader.ParseHeader(headerBytes, ref startIndex);
        }

        public void Dispose()
        {
          FileStream.Dispose();
        }
      }
    }
  }
}
