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

  class ProtocolException : Exception
  {
    public ErrorCode ErrorCode;
    

    public ProtocolException()
    { }

    public ProtocolException(string message)
        : base(message)
    { }

    public ProtocolException(
      string message, 
      ErrorCode errorCode)
        : base(message)
    {
      ErrorCode = errorCode;
    }

    public ProtocolException(string message, Exception inner)
        : base(message, inner)
    { }

  }
}