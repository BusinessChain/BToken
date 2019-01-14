using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace BToken.Chaining
{
  public partial class Blockchain
  {
    class ChainException : Exception
    {
      public ChainCode ErrorCode { get; private set; }


      public ChainException()
      {
      }

      public ChainException(string message)
          : base(message)
      {
      }

      public ChainException(string message, Exception inner)
          : base(message, inner)
      {
      }

      public ChainException(ChainCode errorCode)
      {
        ErrorCode = errorCode;
      }
    }
  }
}