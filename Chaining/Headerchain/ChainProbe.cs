using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using BToken.Networking;

namespace BToken.Chaining
{
  public partial class Headerchain
  {
    class ChainProbe
    {
      public Chain Chain;

      public ChainHeader Header;
      public byte[] Hash;
      public int Depth;


      public ChainProbe(Chain chain)
      {
        Chain = chain;

        Initialize();
      }

      public void Initialize()
      {
        Header = Chain.HeaderTip;
        Hash = Chain.HeaderTipHash;
        Depth = 0;
      }

      public bool GoTo(byte[] hash, ChainHeader stopHeader)
      {
        Initialize();

        while (true)
        {
          if (Hash.IsEqual(hash))
          {
            return true;
          }
          if (stopHeader == Header)
          {
            return false;
          }

          Push();
        }
      }
      public void Push()
      {
        Hash = Header.NetworkHeader.HashPrevious;
        Header = Header.HeaderPrevious;

        Depth++;
      }

      public byte[] GetHeaderHash(ChainHeader header)
      {
        if (header.HeadersNext != null)
        {
          return header.HeadersNext[0].NetworkHeader.HashPrevious;
        }
        else if (header == Chain.HeaderTip)
        {
          return Chain.HeaderTipHash;
        }
        else
        {
          return header.GetHeaderHash();
        }
      }

      public bool IsTip() => Header == Chain.HeaderTip;
      public int GetHeight() => Chain.Height - Depth;
    }
  }
}
