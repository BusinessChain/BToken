using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace BToken.Chaining
{
  class FeeFilterMessage : NetworkMessage
  {
    public ulong FeeFilterValue { get; private set; }

    public FeeFilterMessage(byte[] messagePayload) 
      : base("feefilter", messagePayload)
    { }
  }
}
