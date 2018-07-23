using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace BToken.Chaining
{
  class ChainLinkException : Exception
  {
    public ChainLink ChainLink { get; private set; }


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

    public ChainLinkException(ChainLink chainLink, ChainLinkCode result)
    {
      ChainLink = chainLink;
      HResult = (int)result;
    }
  }
}
