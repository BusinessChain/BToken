using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace BToken.Chaining
{
  enum ErrorCode {
    DUPLICATE,
    ORPHAN,
    INVALID };

  class ChainException : Exception
  {
    public ErrorCode ErrorCode;
    

    public ChainException()
    { }

    public ChainException(string message)
        : base(message)
    { }

    public ChainException(
      string message, 
      ErrorCode errorCode)
        : base(message)
    {
      ErrorCode = errorCode;
    }

    public ChainException(string message, Exception inner)
        : base(message, inner)
    { }

  }
}