using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using BToken.Networking;

namespace BToken.Accounting
{
  class BlockMessage : NetworkMessage
  {
    public NetworkBlock NetworkBlock { get; private set; }


    public BlockMessage(NetworkMessage message) : base("block", message.Payload)
    {
      NetworkBlock = NetworkBlock.ReadBlock(Payload);
    }
  }
}
