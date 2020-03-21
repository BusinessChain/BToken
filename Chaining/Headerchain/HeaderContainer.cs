using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using System.Security.Cryptography;

namespace BToken.Chaining
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



    public override void Parse(SHA256 sHA256)
    {
      int bufferIndex = 0;

      int headersCount = VarInt.GetInt32(
        Buffer, ref bufferIndex);

      if (headersCount == 0)
      {
        return;
      }

      CountItems += headersCount;

      HeaderRoot = Header.ParseHeader(
        Buffer,
        ref bufferIndex,
        sHA256);

      bufferIndex += 1; // skip txCount

      headersCount -= 1;

      HeaderTip = HeaderRoot;

      ParseHeaders(
        ref bufferIndex,
        headersCount,
        sHA256);


      while (bufferIndex < Buffer.Length)
      {
        headersCount = VarInt.GetInt32(Buffer, ref bufferIndex);

        CountItems += headersCount;

        ParseHeaders(
          ref bufferIndex,
          headersCount,
          sHA256);
      }
    }

    void ParseHeaders(
      ref int startIndex,
      int headersCount,
      SHA256 sHA256)
    {
      while (headersCount > 0)
      {
        var header = Header.ParseHeader(
          Buffer,
          ref startIndex,
          sHA256);

        startIndex += 1; // skip txCount

        if (!header.HashPrevious.IsEqual(HeaderTip.Hash))
        {
          throw new ChainException(
            string.Format(
              "header {0} with header hash previous {1} " +
              "not in consecutive order with current tip header {2}",
              header.Hash.ToHexString(),
              header.HashPrevious.ToHexString(),
              HeaderTip.Hash.ToHexString()));
        }

        headersCount -= 1;

        header.HeaderPrevious = HeaderTip;
        HeaderTip = header;
      }
    }
  }
}
