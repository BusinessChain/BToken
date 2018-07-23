using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace BToken
{
  class ChainException : Exception
  {
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
  }
}
