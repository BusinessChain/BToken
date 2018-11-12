using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace BToken.Chaining
{
  class HeaderchainException : Exception
  {
    public BlockCode ErrorCode { get; private set; }


    public HeaderchainException()
    {
    }

    public HeaderchainException(string message)
        : base(message)
    {
    }

    public HeaderchainException(string message, Exception inner)
        : base(message, inner)
    {
    }

    public HeaderchainException(BlockCode errorCode)
    {
      ErrorCode = errorCode;
    }
  }
}
