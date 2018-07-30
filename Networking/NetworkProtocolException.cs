using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace BToken
{
  class NetworkProtocolException : Exception
  {
    public NetworkProtocolException()
    {
    }

    public NetworkProtocolException(string message)
        : base(message)
    {
    }

    public NetworkProtocolException(string message, Exception inner)
        : base(message, inner)
    {
    }
  }
}
