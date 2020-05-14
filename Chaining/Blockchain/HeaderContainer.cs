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
    readonly byte[] ZERO_HASH = new byte[32];

    public Header HeaderRoot;
    public Header HeaderTip;
    public int Count;

    public byte[] Buffer;
    public int IndexBuffer;



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


    public void Parse(SHA256 sHA256)
    {
      IndexBuffer = 0;

      int headersCount = VarInt.GetInt32(
        Buffer, ref IndexBuffer);
      
      HeaderRoot = Header.ParseHeader(
        Buffer,
        ref IndexBuffer,
        sHA256);

      IndexBuffer += 1;

      HeaderTip = HeaderRoot;
      
      Count += 1;

      while (IndexBuffer < Buffer.Length)
      {
        var header = Header.ParseHeader(
          Buffer,
          ref IndexBuffer,
          sHA256);

        IndexBuffer += 1;

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

        header.HeaderPrevious = HeaderTip;
        HeaderTip = header;
      }
    }
  }
}
