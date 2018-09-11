using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace BToken.Networking
{
  public partial class Network
  {
    public class BlockMessage : NetworkMessage
    {
      public NetworkBlock NetworkBlock { get; private set; }


      public BlockMessage(NetworkMessage message) : base("block", message.Payload)
      {
        NetworkBlock = NetworkBlock.ParseBlock(Payload);
      }
    }
  }
}
