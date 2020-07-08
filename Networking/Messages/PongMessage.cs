using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace BToken.Chaining
{
  class PongMessage : NetworkMessage
  {
    public UInt64 Nonce { get; private set; }


    public PongMessage(UInt64 nonce) : base("pong")
    {
      Nonce = nonce;
      Payload = BitConverter.GetBytes(nonce);
    }
  }
}
