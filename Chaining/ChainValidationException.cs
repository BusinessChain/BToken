using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace BToken.Chaining
{
  partial class Blockchain
  {
    protected class ChainLinkException : Exception
    {
      public ChainBlock ChainHeader { get; private set; }
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

      public ChainLinkException(ChainBlock chainHeader, ChainLinkCode errorCode)
      {
        ChainHeader = chainHeader;
        ErrorCode = errorCode;
      }
    }
  }
}
