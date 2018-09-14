using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace BToken.Chaining
{
  class BlockchainException : Exception
  {
    public BlockCode ErrorCode { get; private set; }


    public BlockchainException()
    {
    }

    public BlockchainException(string message)
        : base(message)
    {
    }

    public BlockchainException(string message, Exception inner)
        : base(message, inner)
    {
    }

    public BlockchainException(BlockCode errorCode)
    {
      ErrorCode = errorCode;
    }
  }
}
