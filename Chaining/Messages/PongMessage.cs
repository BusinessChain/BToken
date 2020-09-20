using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace BToken.Chaining
{
  class PongMessage : NetworkMessage
  {
    public PongMessage(UInt64 nonce) : base("pong")
    {
      Payload = BitConverter.GetBytes(nonce);
    }
  }
}
