using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using System.Security.Cryptography;

namespace BToken.Chaining
{
  partial class Headerchain
  {
    public class HeaderContainer : DataContainer
    {
      public IEnumerable<byte[]> LocatorHashes;

      public Header HeaderRoot;
      public Header HeaderTip;



      public HeaderContainer()
      { }

      public HeaderContainer(int index)
        : base(index)
      { }

      public HeaderContainer(int index, byte[] headerBytes)
        : base(index, headerBytes)
      { }

      public HeaderContainer(byte[] headerBytes)
        : base(headerBytes)
      { }


      public HeaderContainer(
        IEnumerable<byte[]> locatorHashes)
      {
        LocatorHashes = locatorHashes;
      }



      SHA256 SHA256 = SHA256.Create();

      public override bool TryParse()
      {
        try
        {
          int bufferIndex = 0;

          int headersCount = VarInt.GetInt32(Buffer, ref bufferIndex);

          if (headersCount == 0)
          {
            return true;
          }

          CountItems += headersCount;

          HeaderRoot = Header.ParseHeader(
            Buffer, 
            ref bufferIndex, 
            SHA256);

          bufferIndex += 1; // skip txCount

          ValidateHeader(HeaderRoot);

          headersCount -= 1;

          HeaderTip = HeaderRoot;

          ParseHeaders(ref bufferIndex, headersCount);


          while (bufferIndex < Buffer.Length)
          {
            headersCount = VarInt.GetInt32(Buffer, ref bufferIndex);

            CountItems += headersCount;

            ParseHeaders(ref bufferIndex, headersCount);
          }
        }
        catch (Exception ex)
        {
          IsValid = false;

          Console.WriteLine(
            "Exception {0} loading archive {1}: {2}",
            ex.GetType().Name,
            Index,
            ex.Message);

          return false;
        }

        return true;
      }

      void ParseHeaders(ref int startIndex, int headersCount)
      {
        while (headersCount > 0)
        {
          var header = Header.ParseHeader(Buffer, ref startIndex, SHA256);
          startIndex += 1; // skip txCount

          ValidateHeader(header);

          if (!header.HashPrevious.IsEqual(HeaderTip.HeaderHash))
          {
            throw new ChainException(
              string.Format("header {0} with header hash previous {1} not in consecutive order with current tip header {2}",
                header.HeaderHash.ToHexString(),
                header.HashPrevious.ToHexString(),
                HeaderTip.HeaderHash.ToHexString()));
          }

          headersCount -= 1;

          header.HeaderPrevious = HeaderTip;
          HeaderTip.HeadersNext.Add(header);

          HeaderTip = header;
        }
      }



      void ValidateHeader(Header header)
      {
        if (header.HeaderHash.IsGreaterThan(header.NBits))
        {
          throw new ChainException(
            string.Format("header hash {0} greater than NBits {1}",
              header.HeaderHash.ToHexString(),
              header.NBits));
        }

        const long MAX_FUTURE_TIME_SECONDS = 2 * 60 * 60;
        bool IsTimestampPremature = header.UnixTimeSeconds >
          (DateTimeOffset.UtcNow.ToUnixTimeSeconds() + MAX_FUTURE_TIME_SECONDS);
        if (IsTimestampPremature)
        {
          throw new ChainException(
            string.Format("Timestamp premature {0}",
              new DateTime(header.UnixTimeSeconds).Date));
        }
      }
    }
  }
}
