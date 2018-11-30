﻿using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace BToken.Networking
{
  public partial class Network
  {
    class FeeFilterMessage : NetworkMessage
    {
      public ulong FeeFilterValue { get; private set; }

      public FeeFilterMessage(NetworkMessage message) : base("feefilter", message.Payload)
      {
      }
    }
  }
}
