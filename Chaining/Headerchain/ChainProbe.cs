using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using BToken.Networking;

namespace BToken.Chaining
{
  partial class Headerchain
  {
    class ChainProbe
    {
      public Chain Chain;

      public Header Header;
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
        Hash = Chain.HeaderTip.HeaderHash;
        Depth = 0;
      }

      public bool GoTo(byte[] hash, Header stopHeader)
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
        Hash = Header.HashPrevious;
        Header = Header.HeaderPrevious;

        Depth++;
      }
      
      public bool IsTip() => Header == Chain.HeaderTip;
      public int GetHeight() => Chain.Height - Depth;
    }
  }
}
