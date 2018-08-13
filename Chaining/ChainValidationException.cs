using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace BToken.Chaining
{
  abstract partial class Chain
  {
    protected class ChainLinkException : Exception
    {
      public ChainLink ChainLink { get; private set; }
      public ChainLinkCode ErrorCode { get; private set; }


      public ChainLinkException()
      {
      }

      public ChainLinkException(string message)
          : base(message)
      {
      }

      public ChainLinkException(string message, Exception inner)
          : base(message, inner)
      {
      }

      public ChainLinkException(ChainLink chainLink, ChainLinkCode errorCode)
      {
        ChainLink = chainLink;
        ErrorCode = errorCode;
      }
    }
  }
}
